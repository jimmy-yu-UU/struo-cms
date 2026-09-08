// tests/Struo.Tests/Query/ItemServiceMultiValueTests.cs
using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Struo.Domain.Auditing;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Validates the multi-value (MultiSelect/CheckboxGroup/Tags) branch in <c>ItemService.Deserialize</c>:
/// option membership, tag non-blank values, Required-as-non-empty-list,
/// de-duplication, and blank-tag-label coercion to null. Uses a dedicated <c>MvThing</c> collection
/// (not the sample Article) so a Required multi-value field can be exercised without affecting other
/// tests. Harness mirrors <see cref="ItemServiceMaxLengthTests"/>.
/// </summary>
public class ItemServiceMultiValueTests : IDisposable
{
    [SugarTable("mv_thing")]
    [CmsCollection("MvThing")]
    public sealed class MvThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Regions", Interface = FieldInterface.MultiSelect, Required = true)]
        [CmsOptions("apac:APAC", "emea:EMEA", "amer")]
        public List<string> Regions { get; set; } = [];

        [CmsField(Label = "Audiences", Interface = FieldInterface.CheckboxGroup)]
        [CmsOptions("b2b:B2B", "b2c:B2C")]
        public List<string> Audiences { get; set; } = [];

        [CmsField(Label = "Keywords", Interface = FieldInterface.Tags, Required = true)]
        public List<TagItem> Keywords { get; set; } = [];
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceMultiValueTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<MvThing>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(MvThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["mvthing"] = typeof(MvThing),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            revisionUser, revisionStore, snapshotBuilder, new NoopUserSessionRevocationService());
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    private static object[] OneKeyword => [new { value = "seed" }];

    [Fact]
    public async Task Create_round_trips_all_three_multi_value_shapes()
    {
        var body = Body(new
        {
            regions = new[] { "apac", "emea" },
            audiences = new[] { "b2b" },
            keywords = new object[] { new { value = "tech" }, new { value = "ai", label = "人工智慧" } },
        });
        var created = await _svc.CreateAsync("mvthing", body);
        var id = created["id"]!.ToString()!;

        var read = await _svc.GetAsync("mvthing", id);
        read.Should().NotBeNull();
        ((IEnumerable<string>)read!["regions"]!).Should().Equal("apac", "emea");
        ((IEnumerable<string>)read["audiences"]!).Should().Equal("b2b");
        var kws = ((IEnumerable<TagItem>)read["keywords"]!).ToList();
        kws.Should().Equal(new TagItem("tech"), new TagItem("ai", "人工智慧"));
    }

    [Fact]
    public async Task Create_rejects_option_value_not_in_options()
    {
        var body = Body(new { regions = new[] { "apac", "mars" }, keywords = OneKeyword });
        var act = () => _svc.CreateAsync("mvthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'regions' has value 'mars' not in its options.");
    }

    [Fact]
    public async Task Create_rejects_required_multi_value_when_empty()
    {
        var body = Body(new { audiences = new[] { "b2b" }, keywords = OneKeyword }); // regions (required) omitted
        var act = () => _svc.CreateAsync("mvthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'regions' is required.");
    }

    [Fact]
    public async Task Create_rejects_required_tags_when_empty()
    {
        var body = Body(new { regions = new[] { "apac" } }); // keywords (required) omitted
        var act = () => _svc.CreateAsync("mvthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'keywords' is required.");
    }

    [Fact]
    public async Task Create_rejects_tag_with_blank_value()
    {
        var body = Body(new
        {
            regions = new[] { "apac" },
            keywords = new object[] { new { value = "  " } },
        });
        var act = () => _svc.CreateAsync("mvthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'keywords' has a tag with an empty value.");
    }

    [Fact]
    public async Task Create_de_duplicates_and_drops_blank_tag_labels()
    {
        var body = Body(new
        {
            regions = new[] { "apac", "apac", "emea" },
            keywords = new object[] { new { value = "tech", label = " " }, new { value = "tech", label = "X" } },
        });
        var created = await _svc.CreateAsync("mvthing", body);
        var read = await _svc.GetAsync("mvthing", created["id"]!.ToString()!);
        ((IEnumerable<string>)read!["regions"]!).Should().Equal("apac", "emea");
        var kws = ((IEnumerable<TagItem>)read["keywords"]!).ToList();
        kws.Should().ContainSingle();
        kws[0].Should().Be(new TagItem("tech")); // blank label -> null; second "tech" dropped
    }

    [Fact]
    public async Task Update_omitting_the_required_regions_keeps_the_stored_value()
    {
        var created = await _svc.CreateAsync("mvthing", Body(new { regions = new[] { "apac" }, keywords = OneKeyword }));
        var id = created["id"]!.ToString()!;

        var updated = await _svc.UpdateAsync("mvthing", id, Body(new { audiences = new[] { "b2b" } }));

        updated.Should().NotBeNull();
        ((IEnumerable<string>)updated!["regions"]!).Should().Equal("apac");
        ((IEnumerable<string>)updated["audiences"]!).Should().Equal("b2b");
    }

    [Fact]
    public async Task Update_sending_an_empty_array_for_the_required_regions_is_rejected()
    {
        var created = await _svc.CreateAsync("mvthing", Body(new { regions = new[] { "apac" }, keywords = OneKeyword }));
        var id = created["id"]!.ToString()!;

        var act = () => _svc.UpdateAsync("mvthing", id, Body(new { regions = Array.Empty<string>() }));

        await act.Should().ThrowAsync<QueryException>().WithMessage("Field 'regions' is required.");
    }

    [Fact]
    public async Task Update_omitting_the_required_keywords_keeps_the_stored_value()
    {
        var created = await _svc.CreateAsync("mvthing", Body(new { regions = new[] { "apac" }, keywords = OneKeyword }));
        var id = created["id"]!.ToString()!;

        var updated = await _svc.UpdateAsync("mvthing", id, Body(new { audiences = new[] { "b2b" } }));

        updated.Should().NotBeNull();
        var kws = ((IEnumerable<TagItem>)updated!["keywords"]!).ToList();
        kws.Should().Equal(new TagItem("seed"));
        ((IEnumerable<string>)updated["audiences"]!).Should().Equal("b2b");
    }

    [Fact]
    public async Task Update_sending_an_empty_array_for_the_required_keywords_is_rejected()
    {
        var created = await _svc.CreateAsync("mvthing", Body(new { regions = new[] { "apac" }, keywords = OneKeyword }));
        var id = created["id"]!.ToString()!;

        var act = () => _svc.UpdateAsync("mvthing", id, Body(new { keywords = Array.Empty<object>() }));

        await act.Should().ThrowAsync<QueryException>().WithMessage("Field 'keywords' is required.");
    }
}
