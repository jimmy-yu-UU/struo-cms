// tests/Struo.Tests/Query/TranslatableSearchHarness.cs
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;

namespace Struo.Tests.Query;

// A minimal fixture with TWO translatable, searchable fields on its translation sidecar — grepped
// tests/ and samples/ for "Translatable = true": every real fixture (Struo.Sample.Blog's
// Article/ArticleTranslation included) has at most one translatable field that is ALSO Searchable, so
// none of them can exercise FilterTranslator.SearchGroup's translatable-search OR-merge path (two
// adjacent TranslatableLeaf conditionals landing in one implicit search OR group). This fixture exists
// solely for that.
[SugarTable("ts_items")]
[CmsCollection("Ts item")]
public sealed class TsItem : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    [CmsTranslations(typeof(TsItemTranslation))]
    [SugarColumn(IsIgnore = true)]
    public List<TsItemTranslation> Translations { get; set; } = [];
    [CmsField(Label = "Code", Interface = FieldInterface.Text, Searchable = true)]
    public string Code { get; set; } = "";
}

[SugarTable("ts_item_translations")]
public sealed class TsItemTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public Guid TsItemId { get; set; }
    public string Locale { get; set; } = "";
    [CmsField(Label = "Title", Interface = FieldInterface.Text, Searchable = true)]
    public string Title { get; set; } = "";
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Subtitle", Interface = FieldInterface.Text, Searchable = true)]
    public string? Subtitle { get; set; }
}

public sealed class TranslatableSearchHarness : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    public ISqlSugarClient Db { get; }
    internal FilterTranslator Translator { get; }

    public static readonly Type[] Types = [typeof(TsItem), typeof(TsItemTranslation)];

    public TranslatableSearchHarness()
    {
        Db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        foreach (var t in Types) Db.CodeFirst.InitTables(t);
        var collections = MetadataScanner.ScanTypes(Types);
        IMetadataProvider metadata = new CachedMetadataProvider(collections);
        IEntityRegistry registry = new EntityRegistry(MetadataScanner.ScanDescriptors(Types));
        var graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["tsItem"] = typeof(TsItem),
        });
        Translator = new FilterTranslator(Db, graph, metadata, registry, new StruoQueryOptions());
    }

    public void Dispose() => _file.Dispose();
}
