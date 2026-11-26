using Struo.Tests.Support.Seeding;
using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// A <c>ReadOnly</c> field with no setter is accepted by the scan and computed by the server: a create
/// stores the item and returns the field.
/// </summary>
public sealed class ItemServiceComputedFieldTests : IDisposable
{
    [SugarTable("computed_field_thing")]
    [CmsCollection("ComputedFieldThing")]
    public sealed class ComputedFieldThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
        public string Name { get; set; } = "";

        [CmsField(Label = "Code", Interface = FieldInterface.Text, ReadOnly = true)]
        public string Code => $"C-{Name}";
    }

    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ItemService _svc;

    public ItemServiceComputedFieldTests()
    {
        var db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<ComputedFieldThing>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        var types = new[] { typeof(ComputedFieldThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["computedfieldthing"] = typeof(ComputedFieldThing),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new Struo.Application.Configuration.StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new Struo.Application.Configuration.StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        _svc = TestItemService.Create(
            repo, provider, registry, graph, expander, graph, languages, revisionUser, revisionStore, snapshotBuilder);
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task A_create_stores_the_item_and_returns_the_computed_field()
    {
        using var doc = JsonDocument.Parse("""{"name":"abc"}""");

        var created = await _svc.CreateAsync("computedfieldthing", doc.RootElement);
        var read = await _svc.GetAsync("computedfieldthing", created["id"]!.ToString()!);

        read.Should().NotBeNull();
        read!["name"].Should().Be("abc");
        read["code"].Should().Be("C-abc");
    }
}
