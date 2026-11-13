// tests/Struo.Tests/Query/PurgeTrashedRestrictKeyNullabilityTests.cs
#nullable enable
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Struo.Tests.Support.Seeding;
using Xunit;

namespace Struo.Tests.Query;

[SugarTable("test_string_targets")]
[CmsCollection("StringTarget")]
internal sealed class StringTarget
{
    [SugarColumn(IsPrimaryKey = true)] public string Id { get; set; } = "";

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; } = "";
}

// The five sources differ only in how the foreign key column gets its nullability: a nullable-annotated
// string, a non-nullable string, and, in nullable-oblivious code, a bare string and one with each explicit
// [SugarColumn(IsNullable = ...)] value.
[SugarTable("test_str_src_annotated_nullable")]
[CmsCollection("StrSrcAnnotatedNullable")]
internal sealed class StrSrcAnnotatedNullable : ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public string Id { get; set; } = "";
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; } = "";

    public string? TargetId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(TargetId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public StringTarget? Target { get; set; }
}

[SugarTable("test_str_src_annotated_required")]
[CmsCollection("StrSrcAnnotatedRequired")]
internal sealed class StrSrcAnnotatedRequired : ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public string Id { get; set; } = "";
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; } = "";

    public string TargetId { get; set; } = "";

    [Navigate(NavigateType.OneToOne, nameof(TargetId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public StringTarget? Target { get; set; }
}

#nullable disable

[SugarTable("test_str_src_oblivious_plain")]
[CmsCollection("StrSrcObliviousPlain")]
internal sealed class StrSrcObliviousPlain : ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public string Id { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; }

    public string TargetId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(TargetId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public StringTarget Target { get; set; }
}

[SugarTable("test_str_src_oblivious_null")]
[CmsCollection("StrSrcObliviousNullable")]
internal sealed class StrSrcObliviousNullable : ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public string Id { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; }

    [SugarColumn(IsNullable = true)]
    public string TargetId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(TargetId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public StringTarget Target { get; set; }
}

[SugarTable("test_str_src_oblivious_notnull")]
[CmsCollection("StrSrcObliviousRequired")]
internal sealed class StrSrcObliviousRequired : ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public string Id { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; }

    [SugarColumn(IsNullable = false)]
    public string TargetId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(TargetId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public StringTarget Target { get; set; }
}

#nullable enable

/// <summary>
/// Whether the purge clears or refuses a trashed Restrict reference follows the foreign key column's
/// nullability as the ORM builds it, for Guid and string keys alike.
/// </summary>
public sealed class PurgeTrashedRestrictKeyNullabilityTests : IDisposable
{
    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();
    private readonly ItemService _service;
    private readonly IItemRepository _repository;

    public PurgeTrashedRestrictKeyNullabilityTests()
    {
        var currentUser = new TestCurrentUserAccessor(Guid.Empty);
        var db = SqlSugarClientFactory.Create(_database.Options(), currentUser);
        db.CodeFirst.InitTables<StringTarget>();
        db.CodeFirst.InitTables<StrSrcAnnotatedNullable>();
        db.CodeFirst.InitTables<StrSrcAnnotatedRequired>();
        db.CodeFirst.InitTables<StrSrcObliviousPlain>();
        db.CodeFirst.InitTables<StrSrcObliviousNullable>();
        db.CodeFirst.InitTables<StrSrcObliviousRequired>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        var types = new[]
        {
            typeof(StringTarget), typeof(StrSrcAnnotatedNullable), typeof(StrSrcAnnotatedRequired),
            typeof(StrSrcObliviousPlain), typeof(StrSrcObliviousNullable), typeof(StrSrcObliviousRequired),
        };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["stringTarget"] = typeof(StringTarget),
            ["strSrcAnnotatedNullable"] = typeof(StrSrcAnnotatedNullable),
            ["strSrcAnnotatedRequired"] = typeof(StrSrcAnnotatedRequired),
            ["strSrcObliviousPlain"] = typeof(StrSrcObliviousPlain),
            ["strSrcObliviousNullable"] = typeof(StrSrcObliviousNullable),
            ["strSrcObliviousRequired"] = typeof(StrSrcObliviousRequired),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        _repository = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        _service = TestItemService.Create(
            _repository, provider, registry, graph,
            new RelationExpander(_repository, graph, new StruoQueryOptions()), graph,
            new LanguageProvider(db), currentUser, new SqlSugarRevisionStore(db, currentUser),
            new RevisionSnapshotBuilder(_repository, provider, registry, graph));
    }

    public void Dispose() => _database.Dispose();

    private static object NewSource(string collection, string id, string targetId) => collection switch
    {
        "strSrcAnnotatedNullable" => new StrSrcAnnotatedNullable { Id = id, Name = "s", TargetId = targetId },
        "strSrcAnnotatedRequired" => new StrSrcAnnotatedRequired { Id = id, Name = "s", TargetId = targetId },
        "strSrcObliviousPlain" => new StrSrcObliviousPlain { Id = id, Name = "s", TargetId = targetId },
        "strSrcObliviousNullable" => new StrSrcObliviousNullable { Id = id, Name = "s", TargetId = targetId },
        "strSrcObliviousRequired" => new StrSrcObliviousRequired { Id = id, Name = "s", TargetId = targetId },
        _ => throw new ArgumentOutOfRangeException(nameof(collection)),
    };

    private static string? TargetOf(object? row) => row switch
    {
        StrSrcAnnotatedNullable r => r.TargetId,
        StrSrcAnnotatedRequired r => r.TargetId,
        StrSrcObliviousPlain r => r.TargetId,
        StrSrcObliviousNullable r => r.TargetId,
        StrSrcObliviousRequired r => r.TargetId,
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    [Theory]
    [InlineData("strSrcAnnotatedNullable", true)]
    [InlineData("strSrcAnnotatedRequired", false)]
    [InlineData("strSrcObliviousPlain", false)]
    [InlineData("strSrcObliviousNullable", true)]
    [InlineData("strSrcObliviousRequired", false)]
    public async Task A_trashed_string_key_is_cleared_when_its_column_is_nullable_and_refused_otherwise(
        string collection, bool nullable)
    {
        var targetId = Guid.NewGuid().ToString("N");
        var sourceId = Guid.NewGuid().ToString("N");
        await _repository.CreateAsync("stringTarget", new StringTarget { Id = targetId, Name = "t" });
        await _repository.CreateAsync(collection, NewSource(collection, sourceId, targetId));
        await _repository.SoftDeleteAsync(collection, sourceId, DateTime.UtcNow, null);

        _repository.IsForeignKeyNullable(collection, "targetId").Should().Be(nullable);
        if (nullable)
        {
            (await _service.DeleteAsync("stringTarget", targetId, purge: true)).Should().BeTrue();
            TargetOf(await _repository.GetByIdAsync(collection, sourceId, DeletedFilter.With)).Should().BeNull();
        }
        else
        {
            await Assert.ThrowsAsync<RelationConflictException>(
                () => _service.DeleteAsync("stringTarget", targetId, purge: true));
            (await _repository.GetByIdAsync("stringTarget", targetId)).Should().NotBeNull();
            TargetOf(await _repository.GetByIdAsync(collection, sourceId, DeletedFilter.With)).Should().Be(targetId);
        }
    }

    [Theory]
    [InlineData("trashableRestrictRef", true)]
    [InlineData("trashableRestrictRefRequired", false)]
    public async Task A_trashed_guid_key_follows_the_same_rule(string collection, bool nullable)
    {
        using var h = PurgeIntegrityHarness.Create();
        var categoryId = Guid.Parse(await h.CreateCategoryAsync("Cat"));
        var id = collection == "trashableRestrictRef"
            ? await h.CreateTrashableRefAsync("R", categoryId)
            : await h.CreateTrashableRefRequiredAsync("R", categoryId);
        await h.Service.DeleteAsync(collection, id);

        var purge = () => h.Service.DeleteAsync("fxCategory", categoryId.ToString(), purge: true);

        if (nullable) (await purge()).Should().BeTrue();
        else await Assert.ThrowsAsync<RelationConflictException>(purge);
    }
}
