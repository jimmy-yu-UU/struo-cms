// tests/Struo.Tests/Query/TreeHarness.cs
using System.Text.Json;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Struo.Tests.Support.Seeding;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;

namespace Struo.Tests.Query;

/// <summary>Self-referencing, soft-deletable node without a version column, so no update of it carries a
/// version compare; the name is unique.</summary>
[SugarTable("test_plain_nodes")]
[SugarIndex("ux_test_plain_nodes_name", nameof(Name), OrderByType.Asc, true)]
[CmsCollection("PlainNode")]
internal sealed class PlainNode : ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    public Guid? ParentId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(ParentId))]
    [CmsRelation(Interface = RelationInterface.TreeSelect, OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public PlainNode? Parent { get; set; }
}

/// <summary>Self-referencing node with a string key, for ids that differ only by case. Created only by the
/// tests that insert its rows.</summary>
[SugarTable("test_str_nodes")]
[CmsCollection("StrNode")]
internal sealed class StrNode
{
    [SugarColumn(IsPrimaryKey = true)] public string Id { get; set; } = "";

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; } = "";

    public string? ParentId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(ParentId))]
    [CmsRelation(Interface = RelationInterface.TreeSelect, OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public StrNode? Parent { get; set; }
}

/// <summary>A collection with no self-reference.</summary>
[SugarTable("test_plain_leaves")]
[CmsCollection("PlainLeaf")]
internal sealed class PlainLeaf
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; } = "";
}

/// <summary>
/// One database, any number of <see cref="ItemService"/> instances over it, each with its own connection
/// and transaction. The services cover the media folder tree (a versioned self-referencing collection),
/// <see cref="PlainNode"/> (an unversioned one) and <see cref="PlainLeaf"/> (none).
/// </summary>
internal sealed class TreeHarness : IDisposable
{
    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();
    private bool _tablesCreated;

    public TreeHarness()
    {
        Setup = Build().Service;
        Reader = SqlSugarClientFactory.Create(_database.Options(), new TestCurrentUserAccessor(Guid.Empty));
    }

    /// <summary>The service that creates the rows a test starts from.</summary>
    public ItemService Setup { get; }

    /// <summary>A separate connection for reads and for changes made outside a request.</summary>
    public ISqlSugarClient Reader { get; }

    public void Dispose() => _database.Dispose();

    public (ItemService Service, CountingItemRepository Repository) Build(
        IItemRepository? wrap = null, IEnumerable<IItemWriteInterceptor>? interceptors = null)
    {
        var currentUser = new TestCurrentUserAccessor(Guid.Empty);
        var db = SqlSugarClientFactory.Create(_database.Options(), currentUser);
        if (!_tablesCreated)
        {
            db.CodeFirst.InitTables<MediaFolder>();
            db.CodeFirst.InitTables<File>();
            db.CodeFirst.InitTables<FileTranslation>();
            db.CodeFirst.InitTables<PlainNode>();
            db.CodeFirst.InitTables<PlainLeaf>();
            db.CodeFirst.InitTables<StrNode>();
            db.CodeFirst.InitTables<Language>();
            db.CodeFirst.InitTables<Revision>();
            LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();
            _tablesCreated = true;
        }

        var types = new[] { typeof(MediaFolder), typeof(File), typeof(PlainNode), typeof(PlainLeaf), typeof(StrNode) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["mediafolder"] = typeof(MediaFolder),
            ["file"] = typeof(File),
            ["plainNode"] = typeof(PlainNode),
            ["plainLeaf"] = typeof(PlainLeaf),
            ["strNode"] = typeof(StrNode),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var counting = new CountingItemRepository(
            wrap ?? new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions()));
        var service = TestItemService.Create(
            counting, provider, registry, graph, new RelationExpander(counting, graph, new StruoQueryOptions()),
            graph, new LanguageProvider(db), currentUser, new SqlSugarRevisionStore(db, currentUser),
            new RevisionSnapshotBuilder(counting, provider, registry, graph), interceptors: interceptors);
        return (service, counting);
    }

    public static JsonElement Body(object obj) => JsonSerializer.SerializeToElement(obj);

    public async Task<Guid> CreateAsync(string collection, string name, Guid? parent = null)
    {
        var created = await Setup.CreateAsync(collection, Body(new { name, parentId = parent }));
        return (Guid)created["id"]!;
    }
}
