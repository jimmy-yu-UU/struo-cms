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

public class ItemServiceFilesFieldTests : IDisposable
{
    [SugarTable("files_thing")]
    [CmsCollection("FilesThing")]
    public sealed class FilesThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Gallery", Interface = FieldInterface.Files)]
        public List<Guid> Gallery { get; set; } = new();

        [CmsField(Label = "Required Gallery", Interface = FieldInterface.Files, Required = true)]
        public List<Guid> RequiredGallery { get; set; } = new();
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceFilesFieldTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<FilesThing>();
        db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(FilesThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["filesthing"] = typeof(FilesThing),
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

    private static readonly string A = "11111111-1111-1111-1111-111111111111";
    private static readonly string B = "22222222-2222-2222-2222-222222222222";
    private static readonly string C = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task Gallery_round_trips_in_order()
    {
        var body = Body(new { gallery = new[] { A, B, C }, requiredGallery = new[] { A } });
        var created = await _svc.CreateAsync("filesthing", body);
        var read = await _svc.GetAsync("filesthing", created["id"]!.ToString()!);

        var gallery = ((IEnumerable<Guid>)read!["gallery"]!).Select(g => g.ToString()).ToList();
        gallery.Should().Equal(A, B, C);
    }

    [Fact]
    public async Task Duplicate_ids_are_deduplicated_keeping_first()
    {
        var body = Body(new { gallery = new[] { A, A, B }, requiredGallery = new[] { A } });
        var created = await _svc.CreateAsync("filesthing", body);
        var read = await _svc.GetAsync("filesthing", created["id"]!.ToString()!);

        var gallery = ((IEnumerable<Guid>)read!["gallery"]!).Select(g => g.ToString()).ToList();
        gallery.Should().Equal(A, B);
    }

    [Fact]
    public async Task Empty_guid_entries_are_dropped()
    {
        var body = Body(new { gallery = new[] { A, Guid.Empty.ToString(), B }, requiredGallery = new[] { A } });
        var created = await _svc.CreateAsync("filesthing", body);
        var read = await _svc.GetAsync("filesthing", created["id"]!.ToString()!);

        var gallery = ((IEnumerable<Guid>)read!["gallery"]!).Select(g => g.ToString()).ToList();
        gallery.Should().Equal(A, B);
    }

    [Fact]
    public async Task Required_empty_gallery_is_rejected()
    {
        var body = Body(new { gallery = new[] { A } }); // requiredGallery omitted
        var act = () => _svc.CreateAsync("filesthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'requiredGallery' is required.");
    }

    [Fact]
    public async Task Non_guid_element_is_rejected_as_bad_request()
    {
        var body = Body(new { gallery = new[] { "not-a-guid" }, requiredGallery = new[] { A } });
        var act = () => _svc.CreateAsync("filesthing", body);
        await act.Should().ThrowAsync<QueryException>(); // STJ JsonException -> QueryException (400), not 500
    }
}
