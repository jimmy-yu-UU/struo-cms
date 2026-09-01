// tests/Struo.Tests/Query/ItemServiceCreateBindingTests.cs
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
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Covers the create-path allowlist in <see cref="ItemDeserializer"/>: a caller with write
/// permission must not be able to set the primary key, the optimistic-concurrency version, the
/// soft-delete markers, or any undeclared CLR property via the create request body — only what
/// <see cref="ItemService.UpdateCoreAsync"/> already allows on update (declared [CmsField]s,
/// declared ManyToOne foreign keys, translations, and M2M relation keys).
/// </summary>
public sealed class ItemServiceCreateBindingTests : IDisposable
{
    // A local fixture mirroring the shape of a fork's own collection: one declared field, one
    // declared ManyToOne relation/FK, soft-delete markers, and (deliberately) a public writable
    // property with no [CmsField] — the "undeclared fork property" case from the defect writeup.
    [SugarTable("create_binding_widget_category")]
    [CmsCollection("CreateBindingWidgetCategory")]
    public sealed class WidgetCategory : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
        public string Name { get; set; } = string.Empty;
    }

    [SugarTable("create_binding_widget")]
    [CmsCollection("CreateBindingWidget")]
    public sealed class Widget : AuditableEntity, ISoftDeletable
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedBy { get; set; }

        [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
        public string Name { get; set; } = string.Empty;

        [SugarColumn(IsNullable = true)]
        public Guid? CategoryId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(CategoryId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull)]
        [SugarColumn(IsIgnore = true)]
        public WidgetCategory? Category { get; set; }

        // No [CmsField]: an internal column that MetadataScanner ignores entirely, so it must never
        // be settable from a client-supplied request body either.
        public string InternalSecret { get; set; } = "server-default";
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly IItemRepository _repo;
    private readonly ItemService _svc;

    public ItemServiceCreateBindingTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<Widget>();
        db.CodeFirst.InitTables<WidgetCategory>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(Widget), typeof(WidgetCategory) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["widget"] = typeof(Widget),
            ["widgetCategory"] = typeof(WidgetCategory),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        _repo = repo;
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, resolver, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            revisionUser, revisionStore, snapshotBuilder, new NoopUserSessionRevocationService());
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Create_ignores_a_client_supplied_id()
    {
        var clientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var body = Body($$"""{"id":"{{clientId}}","name":"Widget A"}""");

        var created = await _svc.CreateAsync("widget", body);

        created["id"].Should().NotBe(clientId);
    }

    [Fact]
    public async Task Create_ignores_a_client_supplied_version()
    {
        var body = Body("""{"name":"Widget A","version":999}""");

        var created = await _svc.CreateAsync("widget", body);

        created["version"].Should().Be(0L);
    }

    [Fact]
    public async Task Create_ignores_a_client_supplied_deletedAt()
    {
        var body = Body("""{"name":"Widget A","deletedAt":"2026-01-01T00:00:00Z","deletedBy":"22222222-2222-2222-2222-222222222222"}""");

        var created = await _svc.CreateAsync("widget", body);
        var id = created["id"]!.ToString()!;

        // Default DeletedFilter.Exclude means a row born soft-deleted would come back null here.
        var reloaded = await _svc.GetAsync("widget", id);
        reloaded.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_ignores_an_undeclared_property()
    {
        var body = Body("""{"name":"Widget A","internalSecret":"client-supplied"}""");

        var created = await _svc.CreateAsync("widget", body);
        var id = created["id"]!.ToString()!;

        // internalSecret has no [CmsField], so it is never projected by ItemService — read the
        // persisted CLR entity directly through the repository instead.
        var entity = (Widget)(await _repo.GetByIdAsync("widget", id))!;
        entity.InternalSecret.Should().Be("server-default");
    }

    [Fact]
    public async Task Create_still_sets_a_declared_many_to_one_foreign_key()
    {
        var catBody = Body("""{"name":"Category A"}""");
        var category = await _svc.CreateAsync("widgetCategory", catBody);
        var categoryId = (Guid)category["id"]!;

        var body = Body("{\"name\":\"Widget A\",\"categoryId\":\"" + categoryId + "\"}");
        var created = await _svc.CreateAsync("widget", body);

        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["category"] = new DeepRelationSpec(null, null)
        });
        var reloaded = await _svc.GetAsync("widget", created["id"]!.ToString()!, deep);

        reloaded.Should().NotBeNull();
        reloaded!.Should().ContainKey("category");
        var loadedCategory = (IReadOnlyDictionary<string, object?>)reloaded["category"]!;
        loadedCategory["id"].Should().Be(categoryId);
    }
}

/// <summary>
/// Broad happy-path check that the create allowlist did not narrow the legitimate write surface —
/// declared fields, translations and an M2M relation still bind exactly as before. Reuses the
/// Article/Category/Tag sample-domain wiring from <c>ItemServiceTests</c>.
/// </summary>
public sealed class ItemServiceCreateBindingHappyPathTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceCreateBindingHappyPathTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<Article>();
        db.CodeFirst.InitTables<ArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Tag>();
        db.CodeFirst.InitTables<ArticleTag>();
        db.CodeFirst.InitTables<Category>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[]
        {
            typeof(Article), typeof(Category), typeof(Tag),
            typeof(Struo.Infrastructure.Files.File), typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article),
            ["category"] = typeof(Category),
            ["tag"] = typeof(Tag),
            ["file"] = typeof(Struo.Infrastructure.Files.File),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, resolver, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            revisionUser, revisionStore, snapshotBuilder, new NoopUserSessionRevocationService());
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Create_still_accepts_declared_fields_translations_and_m2m()
    {
        var category = await _svc.CreateAsync("category", Body("""{"name":"Category A"}"""));
        var categoryId = (Guid)category["id"]!;

        var tag = await _svc.CreateAsync("tag", Body("""{"name":"Tag A"}"""));
        var tagId = tag["id"]!.ToString();

        var body = Body(
            "{\"status\":\"published\",\"categoryId\":\"" + categoryId + "\",\"tags\":[\"" + tagId +
            "\"],\"translations\":{\"en\":{\"title\":\"Hello\"}}}");
        var created = await _svc.CreateAsync("article", body);

        created["status"].Should().Be("published");

        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["category"] = new DeepRelationSpec(null, null),
            ["tags"] = new DeepRelationSpec(null, null),
        });
        var reloaded = await _svc.GetAsync("article", created["id"]!.ToString()!, deep, locale: "en");

        reloaded.Should().NotBeNull();
        var loadedCategory = (IReadOnlyDictionary<string, object?>)reloaded!["category"]!;
        loadedCategory["id"].Should().Be(categoryId);
        var tags = (System.Collections.IEnumerable)reloaded["tags"]!;
        tags.Cast<object>().Should().HaveCount(1);
        var translations = (IReadOnlyDictionary<string, Dictionary<string, object?>>)reloaded["translations"]!;
        translations["en"]["title"].Should().Be("Hello");
    }
}
