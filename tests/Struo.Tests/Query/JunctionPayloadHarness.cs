// tests/Struo.Tests/Query/JunctionPayloadHarness.cs
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Application.Revisions;
using Struo.Application.Security;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;

namespace Struo.Tests.Query;

// ── fixtures ─────────────────────────────────────────────────────────────────

[SugarTable("jp_parents")]
[CmsCollection("Jp parent", Revisions = true)]
public sealed class JpParent : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)] public string Name { get; set; } = "";
    [Navigate(typeof(JpLink), nameof(JpLink.JpParentId), nameof(JpLink.JpChildId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}", SortField = nameof(JpLink.Sort))]
    [SugarColumn(IsIgnore = true)]
    public List<JpChild> Children { get; set; } = [];
    [Navigate(typeof(JpPlainLink), nameof(JpPlainLink.JpParentId), nameof(JpPlainLink.JpChildId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<JpChild> PlainChildren { get; set; } = [];
    [Navigate(typeof(JpAdminLink), nameof(JpAdminLink.JpParentId), nameof(JpAdminLink.JpChildId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<JpChild> AdminChildren { get; set; } = [];
}

[SugarTable("jp_children")]
[CmsCollection("Jp child")]
public sealed class JpChild : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)] public string Name { get; set; } = "";
    /// <summary>A second, deeper payload junction (child -&gt; tag) so a nested `deep` under `children`
    /// exercises `_junction` projection/omission at depth 2, not just the top level.</summary>
    [Navigate(typeof(JpChildLink), nameof(JpChildLink.JpChildId), nameof(JpChildLink.JpTagId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<JpTag> Tags { get; set; } = [];
}

[SugarTable("jp_tags")]
[CmsCollection("Jp tag")]
public sealed class JpTag : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)] public string Name { get; set; } = "";
}

[SugarTable("jp_links")]
[CmsCollection("Jp link", Hidden = true)]
public sealed class JpLink
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    [CmsField(Label = "Parent", Interface = FieldInterface.Uuid)] public Guid JpParentId { get; set; }
    [CmsField(Label = "Child", Interface = FieldInterface.Uuid)] public Guid JpChildId { get; set; }
    [SugarColumn(IsNullable = true)] [CmsField(Label = "Note", Interface = FieldInterface.Text, MaxLength = 20)] public string? Note { get; set; }
    [SugarColumn(IsNullable = true)] [CmsField(Label = "Weight", Interface = FieldInterface.Number)] public int? Weight { get; set; }
    [SugarColumn(IsNullable = true)] [CmsField(Label = "Secret", Interface = FieldInterface.Text, Hidden = true)] public string? Secret { get; set; }
    [CmsField(Label = "Sort", Interface = FieldInterface.Number)] public int Sort { get; set; }
    // T5: a Guid-typed payload field, to prove a non-string payload scalar round-trips through
    // create -> update -> revert (snapshot -> ApplyPayload/CoerceScalar -> DB) intact. An enum-typed
    // payload field was considered too (kind: "B") but dropped: ItemDeserializer.DeserializePartial
    // binds the junction payload element via plain System.Text.Json (JsonSerializerDefaults.Web,
    // no JsonStringEnumConverter), which cannot deserialize a JSON *string* into an enum property —
    // it throws JsonException ("could not be converted"), surfaced as a 400 QueryException. No
    // existing entity in this codebase declares an enum-typed [CmsField] to establish a binding
    // convention, so adding one here would invent a new, untested convention rather than exercise
    // an existing one.
    [SugarColumn(IsNullable = true)] [CmsField(Label = "Ref", Interface = FieldInterface.Uuid)] public Guid? Ref { get; set; }
}

[SugarTable("jp_plain_links")]
public sealed class JpPlainLink
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public Guid JpParentId { get; set; }
    public Guid JpChildId { get; set; }
}

/// <summary>Nested-under-child payload junction (see <see cref="JpChild.Tags"/>).</summary>
[SugarTable("jp_child_links")]
[CmsCollection("Jp child link", Hidden = true)]
public sealed class JpChildLink
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    [CmsField(Label = "Child", Interface = FieldInterface.Uuid)] public Guid JpChildId { get; set; }
    [CmsField(Label = "Tag", Interface = FieldInterface.Uuid)] public Guid JpTagId { get; set; }
    [SugarColumn(IsNullable = true)] [CmsField(Label = "Label", Interface = FieldInterface.Text)] public string? Label { get; set; }
}

/// <summary>A payload junction whose collection is <c>AdminOnly</c> — proves that an object element on
/// such a relation is gated by super-admin, not just the ordinary per-collection write grant.</summary>
[SugarTable("jp_admin_links")]
[CmsCollection("Jp admin link", Hidden = true, AdminOnly = true)]
public sealed class JpAdminLink
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    [CmsField(Label = "Parent", Interface = FieldInterface.Uuid)] public Guid JpParentId { get; set; }
    [CmsField(Label = "Child", Interface = FieldInterface.Uuid)] public Guid JpChildId { get; set; }
    [SugarColumn(IsNullable = true)] [CmsField(Label = "Note", Interface = FieldInterface.Text)] public string? Note { get; set; }
}

// ── harness ───────────────────────────────────────────────────────────────────

/// <summary>Real ItemService over SQLite with a payload junction (JpLink) and a plain one (JpPlainLink),
/// modelled on M2MRevertHarness. Permissions are pluggable so RBAC omission/refusal can be tested.
/// Shared by Tasks 3 (write side), 4 and 5.</summary>
public sealed class JunctionPayloadHarness : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    public ISqlSugarClient Db { get; }
    public ItemService Service { get; }
    public IItemRepository Repository { get; }
    public RelationshipGraph Graph { get; }
    public IMetadataProvider Metadata { get; }
    public IEntityRegistry Registry { get; }
    public IRevisionStore RevisionStore { get; }

    public JunctionPayloadHarness(IPermissionService? permissions = null)
    {
        var currentUser = new TestCurrentUserAccessor(Guid.Empty);
        Db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString }, currentUser);
        Db.CodeFirst.InitTables<Language>();
        Db.CodeFirst.InitTables<Revision>();
        Db.CodeFirst.InitTables<JpParent>();
        Db.CodeFirst.InitTables<JpChild>();
        Db.CodeFirst.InitTables<JpLink>();
        Db.CodeFirst.InitTables<JpPlainLink>();
        Db.CodeFirst.InitTables<JpAdminLink>();
        Db.CodeFirst.InitTables<JpTag>();
        Db.CodeFirst.InitTables<JpChildLink>();
        LanguageSeeder.SeedAsync(Db).GetAwaiter().GetResult();

        var types = new[]
        {
            typeof(JpParent), typeof(JpChild), typeof(JpLink), typeof(JpPlainLink), typeof(JpAdminLink),
            typeof(JpTag), typeof(JpChildLink),
        };
        var collections = MetadataScanner.ScanTypes(types);
        Metadata = new CachedMetadataProvider(collections);
        Registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["jpParent"] = typeof(JpParent), ["jpChild"] = typeof(JpChild), ["jpLink"] = typeof(JpLink),
            ["jpAdminLink"] = typeof(JpAdminLink), ["jpTag"] = typeof(JpTag), ["jpChildLink"] = typeof(JpChildLink),
        };
        Graph = new RelationshipGraph(collections, collectionTypes);
        var options = new StruoQueryOptions();
        var repo = new SqlSugarItemRepository(Db, Registry, Graph, Metadata, options, NullLogger<SqlSugarItemRepository>.Instance);
        var resolver = new RelationFilterResolver(repo, Graph, Metadata, Registry, options);
        var expander = new RelationExpander(repo, Graph, resolver, options);
        var languages = new LanguageProvider(Db);
        var revisionStore = new SqlSugarRevisionStore(Db, currentUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, Metadata, Registry, Graph);
        Repository = repo;
        RevisionStore = revisionStore;
        Service = new ItemService(repo, Metadata, Registry, permissions ?? new AllowAllPermissionService(),
            Graph, expander, Graph, resolver, languages, options, new GanssHtmlSanitizer(),
            currentUser, revisionStore, snapshotBuilder, new NoopUserSessionRevocationService());
    }

    public static JsonElement Body(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    public async Task<Guid> CreateChildAsync(string name, object? tags = null) =>
        Guid.Parse((await Service.CreateAsync(
            "jpChild", Body(tags is null ? new { name } : new { name, tags })))["id"]!.ToString()!);

    public async Task<Guid> CreateParentAsync(object children) =>
        Guid.Parse((await Service.CreateAsync("jpParent", Body(new { name = "p", children })))["id"]!.ToString()!);

    public async Task<Guid> CreateTagAsync(string name) =>
        Guid.Parse((await Service.CreateAsync("jpTag", Body(new { name })))["id"]!.ToString()!);

    public List<JpLink> Links(Guid parentId) =>
        Db.Queryable<JpLink>().Where(l => l.JpParentId == parentId).OrderBy(l => l.Sort).ToList();

    public List<JpAdminLink> AdminLinks(Guid parentId) =>
        Db.Queryable<JpAdminLink>().Where(l => l.JpParentId == parentId).ToList();

    public void Dispose() => _file.Dispose();
}
