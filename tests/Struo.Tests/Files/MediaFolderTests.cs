// tests/Struo.Tests/Files/MediaFolderTests.cs
using Struo.Tests.Support.Seeding;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;

namespace Struo.Tests.Files;

/// <summary>
/// Media folders: MediaFolder core entity — self-referencing tree, Hidden
/// collection routed as <c>mediafolder</c>; File.FolderId nullable organisational FK. Fixture wiring
/// mirrors <c>ItemServiceTests</c>'s hand-wired-services pattern (unit test database + InitTables).
/// </summary>
public sealed class MediaFolderTests : IDisposable
{
    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ItemService _svc;
    private readonly CachedMetadataProvider _metadata;

    public MediaFolderTests()
    {
        var db = SqlSugarClientFactory.Create(
            _file.Options(),
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<MediaFolder>();
        db.CodeFirst.InitTables<File>();
        db.CodeFirst.InitTables<FileTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        var types = new[] { typeof(MediaFolder), typeof(File) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["mediafolder"] = typeof(MediaFolder),
            ["file"] = typeof(File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var currentUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, currentUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);

        _metadata = provider;
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            currentUser, revisionStore, snapshotBuilder, new NoopUserSessionRevocationService());
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement BodyOf(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Mediafolder_collection_is_registered_and_hidden()
    {
        var meta = _metadata.GetCollection("mediafolder");

        meta.Should().NotBeNull();
        meta!.Hidden.Should().BeTrue();
        meta.Group.Should().Be("System");
    }

    [Fact]
    public async Task Create_list_update_folder_roundtrip()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;

        var query = new QueryModel(null, null, [], 0, 0, null);
        var listed = await _svc.QueryAsync("mediafolder", query);
        listed.Data.Should().Contain(d => (Guid)d["id"]! == aId);

        var b = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "B", parentId = aId }));
        var bId = (Guid)b["id"]!;

        var deep = new DeepSpec(
            new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["parent"] = new DeepRelationSpec(null, null)
            });
        var fetchedB = await _svc.GetAsync("mediafolder", bId.ToString(), deep);
        fetchedB.Should().NotBeNull();
        var parent = (IReadOnlyDictionary<string, object?>)fetchedB!["parent"]!;
        parent["id"].Should().Be(aId);

        var updated = await _svc.UpdateAsync("mediafolder", bId.ToString(), BodyOf(new { name = "B2" }));
        updated.Should().NotBeNull();
        updated!["name"].Should().Be("B2");
    }

    // UpdateAsync writes the whole row back, CreatedAt included, so a CreatedAt read with the wrong
    // instant or kind moves on every PATCH.
    [Fact]
    public async Task Updates_leave_CreatedAt_unchanged()
    {
        var before = DateTime.UtcNow;
        var created = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var id = created["id"]!.ToString()!;
        var createdAt = (DateTime)(await _svc.GetAsync("mediafolder", id))!["createdAt"]!;

        await _svc.UpdateAsync("mediafolder", id, BodyOf(new { name = "A2" }));
        await _svc.UpdateAsync("mediafolder", id, BodyOf(new { name = "A3" }));

        var after = (DateTime)(await _svc.GetAsync("mediafolder", id))!["createdAt"]!;
        after.Should().Be(createdAt);
        after.Should().BeCloseTo(before, TimeSpan.FromMinutes(1));
        if (TestBackend.Current is TestBackendKind.PostgreSQL or TestBackendKind.SqlServer)
            after.Kind.Should().Be(DateTimeKind.Utc);
    }

    // A filter value on a zone-aware column is an instant: a trailing Z or offset names it, and a value
    // without one is UTC. The result depends on neither the database server's zone nor the host's.
    [Theory]
    [InlineData("utc")]
    [InlineData("offset")]
    [InlineData("unspecified")]
    public async Task CreatedAt_filters_compare_against_the_utc_instant(string form)
    {
        var aId = await CreateFolderAsync("A");
        await Task.Delay(50);
        var bId = await CreateFolderAsync("B");
        var aCreated = await CreatedAtAsync(aId);
        var bCreated = await CreatedAtAsync(bId);
        var cutoff = aCreated.AddTicks((bCreated - aCreated).Ticks / 2);

        (await FolderIdsAsync(QueryOperator.Gt, cutoff, form)).Should().Equal(bId);
        (await FolderIdsAsync(QueryOperator.Lt, cutoff, form)).Should().Equal(aId);
        (await FolderIdsAsync(QueryOperator.Eq, aCreated, form)).Should().Equal(aId);
    }

    // A text operator is not an instant comparison: a date-prefix _starts_with matches the column's
    // text form where the backend compares it as text. PostgreSQL has no LIKE on timestamptz, and SQL
    // Server's text form of datetimeoffset is its own, so neither is asserted here.
    [Fact]
    public async Task CreatedAt_starts_with_a_date_prefix_matches_as_text()
    {
        if (TestBackend.Current is TestBackendKind.PostgreSQL or TestBackendKind.SqlServer) return;
        var id = await CreateFolderAsync("A");
        var day = (await CreatedAtAsync(id)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var filter = new ComparisonFilter("createdAt", QueryOperator.StartsWith, day);
        var result = await _svc.QueryAsync("mediafolder", new QueryModel(null, filter, [], 0, 0, null));

        result.Data.Select(d => (Guid)d["id"]!).Should().Equal(id);
    }

    // A plain audit column (no zone) takes the same filter forms: Z or offset names the instant, and a
    // value without one is UTC.
    [Theory]
    [InlineData("utc")]
    [InlineData("offset")]
    [InlineData("unspecified")]
    public async Task Plain_CreatedAt_filters_compare_against_the_utc_instant(string form)
    {
        var aId = await InsertFileAsync("a.txt");
        await Task.Delay(50);
        var bId = await InsertFileAsync("b.txt");
        var aCreated = await FileCreatedAtAsync(aId);
        var bCreated = await FileCreatedAtAsync(bId);
        var cutoff = aCreated.AddTicks((bCreated - aCreated).Ticks / 2);

        (await FileIdsAsync(QueryOperator.Gt, cutoff, form)).Should().Equal(bId);
        (await FileIdsAsync(QueryOperator.Lt, cutoff, form)).Should().Equal(aId);
        (await FileIdsAsync(QueryOperator.Eq, aCreated, form)).Should().Equal(aId);
        (await FileIdsAsync(QueryOperator.Neq, aCreated, form)).Should().Equal(bId);

        var inFilter = new ComparisonFilter("createdAt", QueryOperator.In,
            new List<object> { FormatInstant(bCreated, form) });
        var inResult = await _svc.QueryAsync("file", new QueryModel(null, inFilter, [], 0, 0, null));
        inResult.Data.Select(d => (Guid)d["id"]!).Should().Equal(bId);
    }

    // min/max of a zone-aware column come back as UTC instants on every backend.
    [Fact]
    public async Task CreatedAt_min_and_max_are_utc_instants()
    {
        var aId = await CreateFolderAsync("A");
        await Task.Delay(50);
        var bId = await CreateFolderAsync("B");
        var aCreated = await CreatedAtAsync(aId);
        var bCreated = await CreatedAtAsync(bId);

        var query = new QueryModel(null, null, [], 0, 0, null)
        {
            Aggregate = new AggregateSpec(new Dictionary<AggregateOp, IReadOnlyList<string>>
            {
                [AggregateOp.Min] = ["createdAt"],
                [AggregateOp.Max] = ["createdAt"],
            })
        };
        var aggregate = (await _svc.QueryAsync("mediafolder", query)).Aggregate!;

        var min = (DateTime)aggregate.Values[AggregateOp.Min]["createdAt"]!;
        var max = (DateTime)aggregate.Values[AggregateOp.Max]["createdAt"]!;
        min.Should().Be(aCreated);
        max.Should().Be(bCreated);
        if (TestBackend.Current is TestBackendKind.PostgreSQL or TestBackendKind.SqlServer)
        {
            min.Kind.Should().Be(DateTimeKind.Utc);
            max.Kind.Should().Be(DateTimeKind.Utc);
        }
    }

    private async Task<Guid> InsertFileAsync(string name)
    {
        var db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Guid.Empty));
        var file = new File
        {
            Id = Guid.CreateVersion7(),
            StorageKey = name,
            FileName = name,
            ContentType = "text/plain",
            Size = 1,
            Status = "draft",
        };
        await db.Insertable(file).ExecuteCommandAsync();
        return file.Id;
    }

    private async Task<DateTime> FileCreatedAtAsync(Guid id) => DateTime.SpecifyKind(
        (DateTime)(await _svc.GetAsync("file", id.ToString()))!["createdAt"]!, DateTimeKind.Utc);

    private static string FormatInstant(DateTime instant, string form)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return form switch
        {
            "utc" => instant.ToString("O", invariant),
            "offset" => new DateTimeOffset(instant).ToOffset(TimeSpan.FromHours(-5)).ToString("O", invariant),
            _ => instant.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", invariant),
        };
    }

    private async Task<List<Guid>> FileIdsAsync(QueryOperator op, DateTime instant, string form)
    {
        var filter = new ComparisonFilter("createdAt", op, FormatInstant(instant, form));
        var result = await _svc.QueryAsync("file", new QueryModel(null, filter, [], 0, 0, null));
        return result.Data.Select(d => (Guid)d["id"]!).ToList();
    }

    private async Task<Guid> CreateFolderAsync(string name) =>
        (Guid)(await _svc.CreateAsync("mediafolder", BodyOf(new { name })))["id"]!;

    private async Task<DateTime> CreatedAtAsync(Guid id) => DateTime.SpecifyKind(
        (DateTime)(await _svc.GetAsync("mediafolder", id.ToString()))!["createdAt"]!, DateTimeKind.Utc);

    private async Task<List<Guid>> FolderIdsAsync(QueryOperator op, DateTime instant, string form)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var value = form switch
        {
            "utc" => instant.ToString("O", invariant),
            "offset" => new DateTimeOffset(instant).ToOffset(TimeSpan.FromHours(-5)).ToString("O", invariant),
            _ => instant.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", invariant),
        };
        var filter = new ComparisonFilter("createdAt", op, value);
        var result = await _svc.QueryAsync("mediafolder", new QueryModel(null, filter, [], 0, 0, null));
        return result.Data.Select(d => (Guid)d["id"]!).ToList();
    }

    [Fact]
    public async Task Files_filter_by_folderId_eq_and_null()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;

        var db = SqlSugarClientFactory.Create(
            _file.Options(),
            new TestCurrentUserAccessor(Guid.Empty));

        var inFolder = new File
        {
            Id = Guid.CreateVersion7(),
            StorageKey = "k1",
            FileName = "in-folder.txt",
            ContentType = "text/plain",
            Size = 1,
            Status = "draft",
            FolderId = aId,
        };
        var unfiled = new File
        {
            Id = Guid.CreateVersion7(),
            StorageKey = "k2",
            FileName = "unfiled.txt",
            ContentType = "text/plain",
            Size = 1,
            Status = "draft",
            FolderId = null,
        };
        await db.Insertable(inFolder).ExecuteCommandAsync();
        await db.Insertable(unfiled).ExecuteCommandAsync();

        var eqFilter = new ComparisonFilter("folderId", QueryOperator.Eq, aId.ToString());
        var eqResult = await _svc.QueryAsync("file", new QueryModel(null, eqFilter, [], 0, 0, null));
        eqResult.Data.Should().HaveCount(1);
        eqResult.Data[0]["id"].Should().Be(inFolder.Id);

        var nullFilter = new ComparisonFilter("folderId", QueryOperator.Null, null);
        var nullResult = await _svc.QueryAsync("file", new QueryModel(null, nullFilter, [], 0, 0, null));
        nullResult.Data.Should().HaveCount(1);
        nullResult.Data[0]["id"].Should().Be(unfiled.Id);
    }

    [Fact]
    public async Task Delete_folder_with_files_is_rejected_409()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;

        var db = SqlSugarClientFactory.Create(
            _file.Options(),
            new TestCurrentUserAccessor(Guid.Empty));
        var fileInFolder = new File
        {
            Id = Guid.CreateVersion7(),
            StorageKey = "k1",
            FileName = "in-folder.txt",
            ContentType = "text/plain",
            Size = 1,
            Status = "draft",
            FolderId = aId,
        };
        await db.Insertable(fileInFolder).ExecuteCommandAsync();

        await Assert.ThrowsAsync<RelationConflictException>(
            () => _svc.DeleteAsync("mediafolder", aId.ToString()));
    }

    [Fact]
    public async Task Delete_folder_with_subfolder_is_rejected_409()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;
        await _svc.CreateAsync("mediafolder", BodyOf(new { name = "B", parentId = aId }));

        await Assert.ThrowsAsync<RelationConflictException>(
            () => _svc.DeleteAsync("mediafolder", aId.ToString()));
    }

    [Fact]
    public async Task Delete_empty_folder_succeeds()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;

        var deleted = await _svc.DeleteAsync("mediafolder", aId.ToString());

        deleted.Should().BeTrue();
        var fetched = await _svc.GetAsync("mediafolder", aId.ToString());
        fetched.Should().BeNull();
    }
}
