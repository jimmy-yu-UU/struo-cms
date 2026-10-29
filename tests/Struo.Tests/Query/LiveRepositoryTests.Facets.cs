// tests/Struo.Tests/Query/LiveRepositoryTests.Facets.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public sealed partial class LiveRepositoryTests
{
    // Same wiring as BuildRepoWithGraph(), but the entity set is the article/category/tag/tags
    // relation graph these facet/aggregate tests need, plus FxArticleTag registered directly so
    // "fxArticleTag" can be queried as its own collection (registry.Get("fxArticleTag")) rather than
    // only reachable as a relation's junction type. `types`/`collections`/`provider` stay the same
    // shape BuildRepoWithGraph() uses; only the registry's ScanDescriptors input and collectionTypes
    // gain FxArticleTag — RelationshipGraph itself never looks at collectionTypes entries for
    // collections absent from `collections`, so that extra entry is inert there.
    private (IItemRepository Repo, RelationshipGraph Graph, IMetadataProvider Md) BuildFacetRepo()
    {
        _db = CreateClient(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        _db.CodeFirst.InitTables<FxCategory>();
        _db.CodeFirst.InitTables<FxArticle>();
        _db.CodeFirst.InitTables<FxTag>();
        _db.CodeFirst.InitTables<FxArticleTag>();
        _db.CodeFirst.InitTables<FxArticleTranslation>();
        // Deterministic start: clear the four fixture tables this suite writes plus FxCategory.
        _db.Deleteable<FxArticleTag>().Where(x => true).ExecuteCommand();
        _db.Deleteable<FxArticleTranslation>().Where(x => true).ExecuteCommand();
        _db.Deleteable<FxArticle>().Where(x => true).ExecuteCommand();
        _db.Deleteable<FxTag>().Where(x => true).ExecuteCommand();
        _db.Deleteable<FxCategory>().Where(x => true).ExecuteCommand();

        var types = new[]
        {
            typeof(FxArticle), typeof(FxCategory), typeof(FxTag), typeof(Struo.Infrastructure.Files.File),
            typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(
            [typeof(FxArticle), typeof(FxCategory), typeof(FxTag), typeof(FxArticleTag)]));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["fxArticle"] = typeof(FxArticle), ["fxCategory"] = typeof(FxCategory),
            ["fxTag"] = typeof(FxTag), ["fxArticleTag"] = typeof(FxArticleTag),
            ["file"] = typeof(Struo.Infrastructure.Files.File),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var options = new StruoQueryOptions();
        var repo = new SqlSugarItemRepository(_db, registry, graph, provider, options);
        return (repo, graph, provider);
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Facet_by_uuid_fk_groups_nulls_and_orders(string backend)
    {
        if (!Use(backend)) return;
        var (repo, graph, md) = BuildFacetRepo();
        var cat = new FxCategory { Id = Guid.NewGuid(), Name = "Tech" };
        await repo.CreateAsync("fxCategory", cat);
        await repo.CreateAsync("fxArticle", new FxArticle { Id = Guid.NewGuid(), Status = "published", CategoryId = cat.Id });
        await repo.CreateAsync("fxArticle", new FxArticle { Id = Guid.NewGuid(), Status = "published", CategoryId = cat.Id });
        await repo.CreateAsync("fxArticle", new FxArticle { Id = Guid.NewGuid(), Status = "draft" });

        var facet = FacetPathResolver.Resolve(md.GetCollection("fxArticle")!, "categoryId", graph, md);
        var b = await repo.FacetAsync(new FacetRequest("fxArticle", new QueryModel(null, null, [], 100, 0, null), facet, [], null, DeletedFilter.Exclude, 50));
        b.Should().HaveCount(2);
        b[0].Should().Be(new FacetBucket(cat.Id.ToString(), 2));
        b[1].Should().Be(new FacetBucket(null, 1));
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Many_to_many_leaf_facet_counts_distinct_uuid_roots(string backend)
    {
        if (!Use(backend)) return;
        var (repo, graph, md) = BuildFacetRepo();
        var a1 = new FxArticle { Id = Guid.NewGuid(), Status = "published" };
        var a2 = new FxArticle { Id = Guid.NewGuid(), Status = "published" };
        await repo.CreateAsync("fxArticle", a1); await repo.CreateAsync("fxArticle", a2);
        var guide = new FxTag { Id = Guid.NewGuid(), Name = "Guide" }; var misc = new FxTag { Id = Guid.NewGuid(), Name = "Misc" };
        await repo.CreateAsync("fxTag", guide); await repo.CreateAsync("fxTag", misc);
        _db!.Insertable(new[]
        {
            new FxArticleTag { Id = Guid.NewGuid(), ArticleId = a1.Id, TagId = guide.Id },
            new FxArticleTag { Id = Guid.NewGuid(), ArticleId = a2.Id, TagId = guide.Id },
            new FxArticleTag { Id = Guid.NewGuid(), ArticleId = a2.Id, TagId = misc.Id },
        }).ExecuteCommand();

        var facet = FacetPathResolver.Resolve(md.GetCollection("fxArticle")!, "tags.name", graph, md);
        var b = await repo.FacetAsync(new FacetRequest("fxArticle", new QueryModel(null, null, [], 100, 0, null), facet, [], null, DeletedFilter.Exclude, 50));
        b.Select(x => (x.Value, x.Count)).Should().Equal(("Guide", 2), ("Misc", 1));
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Translatable_leaf_facet_reads_the_sidecar(string backend)
    {
        if (!Use(backend)) return;
        var (repo, graph, md) = BuildFacetRepo();
        var cat = new FxCategory { Id = Guid.NewGuid(), Name = "C" };
        await repo.CreateAsync("fxCategory", cat);
        var a1 = new FxArticle { Id = Guid.NewGuid(), Status = "published", CategoryId = cat.Id };
        await repo.CreateAsync("fxArticle", a1);
        _db!.Insertable(new FxArticleTranslation { FxArticleId = a1.Id, Locale = "zh-TW", Title = "甲" }).ExecuteCommand();

        var facet = FacetPathResolver.Resolve(md.GetCollection("fxCategory")!, "articles.title", graph, md);
        var zh = await repo.FacetAsync(new FacetRequest("fxCategory", new QueryModel(null, null, [], 100, 0, null), facet, [], "zh-TW", DeletedFilter.Exclude, 50));
        zh.Should().Equal(new FacetBucket("甲", 1));
        var en = await repo.FacetAsync(new FacetRequest("fxCategory", new QueryModel(null, null, [], 100, 0, null), facet, [], "en", DeletedFilter.Exclude, 50));
        en.Should().Equal(new FacetBucket(null, 1));
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Aggregates_normalise_numeric_and_timestamp_types(string backend)
    {
        if (!Use(backend)) return;
        var (repo, _, _) = BuildFacetRepo();
        var a = new FxArticle { Id = Guid.NewGuid(), Status = "published" };
        await repo.CreateAsync("fxArticle", a);
        var t = new FxTag { Id = Guid.NewGuid(), Name = "T" };
        await repo.CreateAsync("fxTag", t);
        _db!.Insertable(new[]
        {
            new FxArticleTag { Id = Guid.NewGuid(), ArticleId = a.Id, TagId = t.Id, Sort = 1 },
            new FxArticleTag { Id = Guid.NewGuid(), ArticleId = a.Id, TagId = t.Id, Sort = 4 },
        }).ExecuteCommand();

        var spec = new AggregateSpec(new Dictionary<AggregateOp, IReadOnlyList<string>>
        {
            [AggregateOp.Sum] = ["sort"], [AggregateOp.Avg] = ["sort"], [AggregateOp.Min] = ["sort"], [AggregateOp.Count] = ["note"],
        });
        var r = await repo.AggregateAsync("fxArticleTag", new QueryModel(null, null, [], 100, 0, null), spec, [], null, DeletedFilter.Exclude);
        r.Values[AggregateOp.Sum]["sort"].Should().BeOfType<long>().And.Be(5L);
        r.Values[AggregateOp.Avg]["sort"].Should().BeOfType<double>().And.Be(2.5);
        r.Values[AggregateOp.Min]["sort"].Should().BeOfType<int>().And.Be(1);
        r.Values[AggregateOp.Count]["note"].Should().Be(0L);

        var articleSpec = new AggregateSpec(new Dictionary<AggregateOp, IReadOnlyList<string>> { [AggregateOp.Max] = ["createdAt"] });
        var ar = await repo.AggregateAsync("fxArticle", new QueryModel(null, null, [], 100, 0, null), articleSpec, [], null, DeletedFilter.Exclude);
        ar.Values[AggregateOp.Max]["createdAt"].Should().BeOfType<DateTime>();
    }

    [Theory, MemberData(nameof(Backends))]
    public void Average_has_no_integer_division_over_int_long_decimal_and_nullable_columns(string backend)
    {
        if (!Use(backend)) return;
        _db = CreateClient(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        AvgProbeAssertions.SeedAndAssert(_db);
    }

    [Theory, MemberData(nameof(Backends))]
    public void Average_with_a_repeating_mean_is_within_a_ten_thousandth(string backend)
    {
        if (!Use(backend)) return;
        _db = CreateClient(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        AvgProbeAssertions.SeedRepeatingMeanAndAssert(_db);
    }
}
