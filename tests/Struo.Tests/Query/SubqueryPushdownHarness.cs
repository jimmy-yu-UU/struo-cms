// tests/Struo.Tests/Query/SubqueryPushdownHarness.cs
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;

namespace Struo.Tests.Query;

// ── fixtures: an EAV-shaped product catalogue ────────────────────────────────
// product ─M2O→ category; product ─O2M→ property(code, valueNum); product ─M2M(payload note)→ label

[SugarTable("sq_categories")]
[CmsCollection("Sq category")]
public sealed class SqCategory : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
}

[SugarTable("sq_products")]
[CmsCollection("Sq product")]
public sealed class SqProduct : AuditableEntity, ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    [CmsField(Label = "Name", Interface = FieldInterface.Text, Searchable = true)] public string Name { get; set; } = "";
    [SugarColumn(IsNullable = true)] public Guid? CategoryId { get; set; }
    [Navigate(NavigateType.OneToOne, nameof(CategoryId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)] public SqCategory? Category { get; set; }
    [Navigate(NavigateType.OneToMany, nameof(SqProperty.ProductId))]
    [CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Code}")]
    [SugarColumn(IsIgnore = true)] public List<SqProperty> Properties { get; set; } = [];
    [Navigate(typeof(SqProductLabel), nameof(SqProductLabel.ProductId), nameof(SqProductLabel.LabelId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)] public List<SqLabel> Labels { get; set; } = [];
}

[SugarTable("sq_properties")]
[CmsCollection("Sq property")]
public sealed class SqProperty : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    [CmsField(Label = "Code", Interface = FieldInterface.Text)] public string Code { get; set; } = "";
    [CmsField(Label = "Value", Interface = FieldInterface.Number)] public int ValueNum { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? ProductId { get; set; }
    [Navigate(NavigateType.OneToOne, nameof(ProductId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)] public SqProduct? Product { get; set; }
}

[SugarTable("sq_labels")]
[CmsCollection("Sq label")]
public sealed class SqLabel : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
}

[SugarTable("sq_product_labels")]
[CmsCollection("Sq product label", Hidden = true)]
public sealed class SqProductLabel
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    [CmsField(Label = "Product", Interface = FieldInterface.Uuid)] public Guid ProductId { get; set; }
    [CmsField(Label = "Label", Interface = FieldInterface.Uuid)] public Guid LabelId { get; set; }
    [SugarColumn(IsNullable = true)] [CmsField(Label = "Note", Interface = FieldInterface.Text)] public string? Note { get; set; }
}

public sealed class SubqueryPushdownHarness : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    public ISqlSugarClient Db { get; }
    public SqlSugarItemRepository Repo { get; }
    public RelationshipGraph Graph { get; }
    public IMetadataProvider Metadata { get; }
    public IEntityRegistry Registry { get; }
    public StruoQueryOptions Options { get; } = new();
    public int SqlStatements { get; private set; }

    public static readonly Type[] Types = [typeof(SqCategory), typeof(SqProduct), typeof(SqProperty), typeof(SqLabel), typeof(SqProductLabel)];

    public SubqueryPushdownHarness()
    {
        Db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        foreach (var t in Types) Db.CodeFirst.InitTables(t);
        var collections = MetadataScanner.ScanTypes(Types);
        Metadata = new CachedMetadataProvider(collections);
        Registry = new EntityRegistry(MetadataScanner.ScanDescriptors(Types));
        Graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["sqCategory"] = typeof(SqCategory), ["sqProduct"] = typeof(SqProduct), ["sqProperty"] = typeof(SqProperty),
            ["sqLabel"] = typeof(SqLabel), ["sqProductLabel"] = typeof(SqProductLabel),
        });
        Repo = new SqlSugarItemRepository(Db, Registry, Graph, Metadata, Options, NullLogger<SqlSugarItemRepository>.Instance);
        Db.Aop.OnLogExecuting = (_, _) => SqlStatements++;
    }

    public void ResetSqlCount() => SqlStatements = 0;

    // Seeds the canonical EAV scenario and returns the ids the tests assert on.
    public (Guid CrossRow, Guid SameRow, Guid NoProps, Guid NoCategory) SeedEav()
    {
        var tech = new SqCategory { Id = Guid.NewGuid(), Name = "Tech" };
        var archive = new SqCategory { Id = Guid.NewGuid(), Name = "Archive" };
        Db.Insertable(new[] { tech, archive }).ExecuteCommand();

        var crossRow = new SqProduct { Id = Guid.NewGuid(), Name = "cross", CategoryId = tech.Id };   // {vds-v,20} {ptot-w,100}
        var sameRow  = new SqProduct { Id = Guid.NewGuid(), Name = "same",  CategoryId = archive.Id }; // {vds-v,80}
        var noProps  = new SqProduct { Id = Guid.NewGuid(), Name = "bare",  CategoryId = tech.Id };
        var noCat    = new SqProduct { Id = Guid.NewGuid(), Name = "nocat", CategoryId = null };
        Db.Insertable(new[] { crossRow, sameRow, noProps, noCat }).ExecuteCommand();
        Db.Insertable(new[]
        {
            new SqProperty { Id = Guid.NewGuid(), ProductId = crossRow.Id, Code = "vds-v",  ValueNum = 20 },
            new SqProperty { Id = Guid.NewGuid(), ProductId = crossRow.Id, Code = "ptot-w", ValueNum = 100 },
            new SqProperty { Id = Guid.NewGuid(), ProductId = sameRow.Id,  Code = "vds-v",  ValueNum = 80 },
            new SqProperty { Id = Guid.NewGuid(), ProductId = noCat.Id,    Code = "o'neil", ValueNum = 1 },
        }).ExecuteCommand();

        var guide = new SqLabel { Id = Guid.NewGuid(), Name = "Guide" };
        var misc  = new SqLabel { Id = Guid.NewGuid(), Name = "Misc" };
        Db.Insertable(new[] { guide, misc }).ExecuteCommand();
        Db.Insertable(new[]
        {
            new SqProductLabel { Id = Guid.NewGuid(), ProductId = crossRow.Id, LabelId = guide.Id, Note = "hero" },
            new SqProductLabel { Id = Guid.NewGuid(), ProductId = sameRow.Id,  LabelId = guide.Id, Note = "plain" },
            new SqProductLabel { Id = Guid.NewGuid(), ProductId = sameRow.Id,  LabelId = misc.Id,  Note = "hero" },
        }).ExecuteCommand();
        return (crossRow.Id, sameRow.Id, noProps.Id, noCat.Id);
    }

    public async Task<List<Guid>> QueryIdsAsync(FilterNode? filter, string? search = null, DeletedFilter deleted = DeletedFilter.Exclude)
    {
        var q = new QueryModel(null, filter, [], 100, 0, search);
        var r = await Repo.QueryAsync("sqProduct", q, ["name"], null, deleted);
        return r.Rows.Cast<SqProduct>().Select(p => p.Id).ToList();
    }

    public Task<IReadOnlyList<FacetBucket>> FacetAsync(string facet, FilterNode? filter = null, string? search = null,
        DeletedFilter deleted = DeletedFilter.Exclude, int maxValues = 50, string collection = "sqProduct")
    {
        var meta = Metadata.GetCollection(collection)!;
        var resolved = FacetPathResolver.Resolve(meta, facet, Graph, Metadata);
        var q = new QueryModel(null, FacetFilterPruner.Prune(filter, resolved), [], 100, 0, search);
        return Repo.FacetAsync(collection, q, resolved, ["name"], null, deleted, maxValues);
    }

    // searchableFields is only populated with "name" when a search term is actually supplied — an
    // empty list when search is null matches how a caller with no searchable fields configured
    // behaves, and keeps every existing filter-only call exercising exactly that path.
    public Task<AggregateResult> AggregateAsync(
        string collection, AggregateSpec spec, FilterNode? filter = null, DeletedFilter deleted = DeletedFilter.Exclude, string? search = null) =>
        Repo.AggregateAsync(collection, new QueryModel(null, filter, [], 100, 0, search), spec, search is null ? [] : ["name"], null, deleted);

    public void Dispose() => _file.Dispose();
}
