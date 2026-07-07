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

public class ItemServiceKeyValueTests : IDisposable
{
    [SugarTable("kv_thing")]
    [CmsCollection("KvThing")]
    public sealed class KvThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Meta", Interface = FieldInterface.KeyValue)]
        public Dictionary<string, string> Meta { get; set; } = new();

        [CmsField(Label = "Required Meta", Interface = FieldInterface.KeyValue, Required = true)]
        public Dictionary<string, string> RequiredMeta { get; set; } = new();
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceKeyValueTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<KvThing>();
        db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(KvThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["kvthing"] = typeof(KvThing),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph);
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer());
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Key_value_map_round_trips_verbatim()
    {
        var body = Body(new
        {
            meta = new Dictionary<string, string> { ["seo-title"] = "值", ["author"] = "me", ["MyKey"] = "v1" },
            requiredMeta = new Dictionary<string, string> { ["k"] = "v" },
        });
        var created = await _svc.CreateAsync("kvthing", body);
        var read = await _svc.GetAsync("kvthing", created["id"]!.ToString()!);

        var meta = (IDictionary<string, string>)read!["meta"]!;
        meta.Should().ContainKey("seo-title");   // un-camelCased
        meta["seo-title"].Should().Be("值");
        meta["author"].Should().Be("me");
        meta.Should().ContainKey("MyKey");       // mixed-case key would change under camelCase
        meta["MyKey"].Should().Be("v1");
    }

    [Fact]
    public async Task Non_string_value_is_rejected_as_bad_request()
    {
        var body = Body(new
        {
            requiredMeta = new Dictionary<string, string> { ["k"] = "v" },
            meta = new { count = 1 },
        });
        var act = () => _svc.CreateAsync("kvthing", body);
        await act.Should().ThrowAsync<QueryException>();
    }

    [Fact]
    public async Task Blank_key_is_rejected()
    {
        var body = Body(new
        {
            requiredMeta = new Dictionary<string, string> { ["k"] = "v" },
            meta = new Dictionary<string, string> { ["  "] = "x" },
        });
        var act = () => _svc.CreateAsync("kvthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'meta' has an entry with an empty key.");
    }

    [Fact]
    public async Task Required_empty_map_is_rejected()
    {
        var body = Body(new { meta = new Dictionary<string, string> { ["k"] = "v" } }); // requiredMeta omitted
        var act = () => _svc.CreateAsync("kvthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'requiredMeta' is required.");
    }
}
