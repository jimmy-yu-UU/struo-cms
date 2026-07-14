using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class ItemServiceJsonFieldTests : IDisposable
{
    [SugarTable("json_thing")]
    [CmsCollection("JsonThing")]
    public sealed class JsonThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Attributes", Interface = FieldInterface.Json)]
        public string? Attributes { get; set; }

        [CmsField(Label = "Required Data", Interface = FieldInterface.Json, Required = true)]
        public string? RequiredData { get; set; }
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceJsonFieldTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<JsonThing>();
        db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(JsonThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["jsonthing"] = typeof(JsonThing),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, resolver, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            new TestCurrentUserAccessor(Guid.Empty));
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Object_array_and_scalar_json_round_trip_as_structured_json()
    {
        var body = Body(new
        {
            attributes = new { a = 1, nested = new { x = "人工智慧" }, arr = new[] { 1, 2 } },
            requiredData = new[] { "x", "y" },
        });
        var created = await _svc.CreateAsync("jsonthing", body);
        var read = await _svc.GetAsync("jsonthing", created["id"]!.ToString()!);

        read.Should().NotBeNull();
        var attrs = (JsonElement)read!["attributes"]!;
        attrs.ValueKind.Should().Be(JsonValueKind.Object);
        attrs.GetProperty("nested").GetProperty("x").GetString().Should().Be("人工智慧");
        attrs.GetProperty("arr").GetArrayLength().Should().Be(2);

        var req = (JsonElement)read["requiredData"]!;
        req.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Absent_optional_json_projects_as_null()
    {
        var body = Body(new { requiredData = new { ok = true } }); // attributes omitted
        var created = await _svc.CreateAsync("jsonthing", body);
        var read = await _svc.GetAsync("jsonthing", created["id"]!.ToString()!);
        read!["attributes"].Should().BeNull();
    }

    [Fact]
    public async Task Required_json_absent_is_rejected()
    {
        var body = Body(new { attributes = new { a = 1 } }); // requiredData (required) omitted
        var act = () => _svc.CreateAsync("jsonthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'requiredData' is required.");
    }
}
