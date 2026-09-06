// tests/Struo.Tests/Query/PostgresIntegrationTests.Facets.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public sealed partial class PostgresIntegrationTests
{
    // Same wiring as BuildRepoWithGraph(), but the entity set is the article/category/tag/tags
    // relation graph these facet/aggregate tests need, plus ArticleTag registered directly so
    // "articleTag" can be queried as its own collection (registry.Get("articleTag")) rather than
    // only reachable as a relation's junction type. `types`/`collections`/`provider` stay the same
    // shape BuildRepoWithGraph() uses; only the registry's ScanDescriptors input and collectionTypes
    // gain ArticleTag — RelationshipGraph itself never looks at collectionTypes entries for
    // collections absent from `collections`, so that extra entry is inert there.
    private (IItemRepository Repo, RelationshipGraph Graph, IMetadataProvider Md) BuildFacetRepo()
    {
        GuardDisposableDatabase();

        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.PostgreSQL, ConnectionString = Conn! },
            new TestCurrentUserAccessor(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        try { _db.DbMaintenance.CreateDatabase(); } catch { /* already exists / not permitted */ }
        _db.CodeFirst.InitTables<Category>();
        _db.CodeFirst.InitTables<Article>();
        _db.CodeFirst.InitTables<Tag>();
        _db.CodeFirst.InitTables<ArticleTag>();
        _db.CodeFirst.InitTables<ArticleTranslation>();
        // Deterministic start: clear the four sample tables this suite writes plus Category.
        _db.Deleteable<ArticleTag>().Where(x => true).ExecuteCommand();
        _db.Deleteable<ArticleTranslation>().Where(x => true).ExecuteCommand();
        _db.Deleteable<Article>().Where(x => true).ExecuteCommand();
        _db.Deleteable<Tag>().Where(x => true).ExecuteCommand();
        _db.Deleteable<Category>().Where(x => true).ExecuteCommand();

        var types = new[]
        {
            typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File),
            typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(
            [typeof(Article), typeof(Category), typeof(Tag), typeof(ArticleTag)]));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category),
            ["tag"] = typeof(Tag), ["articleTag"] = typeof(ArticleTag),
            ["file"] = typeof(Struo.Infrastructure.Files.File),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var options = new StruoQueryOptions();
        var repo = new SqlSugarItemRepository(_db, registry, graph, provider, options);
        return (repo, graph, provider);
    }

    [Fact]
    public async Task Facet_by_uuid_fk_groups_nulls_and_orders_on_postgres()
    {
        if (!PgConfigured) return;
        var (repo, graph, md) = BuildFacetRepo();
        var cat = new Category { Id = Guid.NewGuid(), Name = "Tech" };
        await repo.CreateAsync("category", cat);
        await repo.CreateAsync("article", new Article { Id = Guid.NewGuid(), Status = "published", CategoryId = cat.Id });
        await repo.CreateAsync("article", new Article { Id = Guid.NewGuid(), Status = "published", CategoryId = cat.Id });
        await repo.CreateAsync("article", new Article { Id = Guid.NewGuid(), Status = "draft" });

        var facet = FacetPathResolver.Resolve(md.GetCollection("article")!, "categoryId", graph, md);
        var b = await repo.FacetAsync(new FacetRequest("article", new QueryModel(null, null, [], 100, 0, null), facet, [], null, DeletedFilter.Exclude, 50));
        b.Should().HaveCount(2);
        b[0].Should().Be(new FacetBucket(cat.Id.ToString(), 2));
        b[1].Should().Be(new FacetBucket(null, 1));
    }

    [Fact]
    public async Task Many_to_many_leaf_facet_counts_distinct_uuid_roots_on_postgres()
    {
        if (!PgConfigured) return;
        var (repo, graph, md) = BuildFacetRepo();
        var a1 = new Article { Id = Guid.NewGuid(), Status = "published" };
        var a2 = new Article { Id = Guid.NewGuid(), Status = "published" };
        await repo.CreateAsync("article", a1); await repo.CreateAsync("article", a2);
        var guide = new Tag { Id = Guid.NewGuid(), Name = "Guide" }; var misc = new Tag { Id = Guid.NewGuid(), Name = "Misc" };
        await repo.CreateAsync("tag", guide); await repo.CreateAsync("tag", misc);
        _db!.Insertable(new[]
        {
            new ArticleTag { Id = Guid.NewGuid(), ArticleId = a1.Id, TagId = guide.Id },
            new ArticleTag { Id = Guid.NewGuid(), ArticleId = a2.Id, TagId = guide.Id },
            new ArticleTag { Id = Guid.NewGuid(), ArticleId = a2.Id, TagId = misc.Id },
        }).ExecuteCommand();

        var facet = FacetPathResolver.Resolve(md.GetCollection("article")!, "tags.name", graph, md);
        var b = await repo.FacetAsync(new FacetRequest("article", new QueryModel(null, null, [], 100, 0, null), facet, [], null, DeletedFilter.Exclude, 50));
        b.Select(x => (x.Value, x.Count)).Should().Equal(("Guide", 2), ("Misc", 1));
    }

    [Fact]
    public async Task Translatable_leaf_facet_reads_the_sidecar_on_postgres()
    {
        if (!PgConfigured) return;
        var (repo, graph, md) = BuildFacetRepo();
        var cat = new Category { Id = Guid.NewGuid(), Name = "C" };
        await repo.CreateAsync("category", cat);
        var a1 = new Article { Id = Guid.NewGuid(), Status = "published", CategoryId = cat.Id };
        await repo.CreateAsync("article", a1);
        _db!.Insertable(new ArticleTranslation { ArticleId = a1.Id, Locale = "zh-TW", Title = "甲" }).ExecuteCommand();

        var facet = FacetPathResolver.Resolve(md.GetCollection("category")!, "articles.title", graph, md);
        var zh = await repo.FacetAsync(new FacetRequest("category", new QueryModel(null, null, [], 100, 0, null), facet, [], "zh-TW", DeletedFilter.Exclude, 50));
        zh.Should().Equal(new FacetBucket("甲", 1));
        var en = await repo.FacetAsync(new FacetRequest("category", new QueryModel(null, null, [], 100, 0, null), facet, [], "en", DeletedFilter.Exclude, 50));
        en.Should().Equal(new FacetBucket(null, 1));
    }

    [Fact]
    public async Task Aggregates_normalise_postgres_numeric_and_timestamp_types()
    {
        if (!PgConfigured) return;
        var (repo, _, _) = BuildFacetRepo();
        var a = new Article { Id = Guid.NewGuid(), Status = "published" };
        await repo.CreateAsync("article", a);
        var t = new Tag { Id = Guid.NewGuid(), Name = "T" };
        await repo.CreateAsync("tag", t);
        _db!.Insertable(new[]
        {
            new ArticleTag { Id = Guid.NewGuid(), ArticleId = a.Id, TagId = t.Id, Sort = 1 },
            new ArticleTag { Id = Guid.NewGuid(), ArticleId = a.Id, TagId = t.Id, Sort = 4 },
        }).ExecuteCommand();

        var spec = new AggregateSpec(new Dictionary<AggregateOp, IReadOnlyList<string>>
        {
            [AggregateOp.Sum] = ["sort"], [AggregateOp.Avg] = ["sort"], [AggregateOp.Min] = ["sort"], [AggregateOp.Count] = ["note"],
        });
        var r = await repo.AggregateAsync("articleTag", new QueryModel(null, null, [], 100, 0, null), spec, [], null, DeletedFilter.Exclude);
        r.Values[AggregateOp.Sum]["sort"].Should().BeOfType<long>().And.Be(5L);
        r.Values[AggregateOp.Avg]["sort"].Should().BeOfType<double>().And.Be(2.5);
        r.Values[AggregateOp.Min]["sort"].Should().BeOfType<int>().And.Be(1);
        r.Values[AggregateOp.Count]["note"].Should().Be(0L);

        var articleSpec = new AggregateSpec(new Dictionary<AggregateOp, IReadOnlyList<string>> { [AggregateOp.Max] = ["createdAt"] });
        var ar = await repo.AggregateAsync("article", new QueryModel(null, null, [], 100, 0, null), articleSpec, [], null, DeletedFilter.Exclude);
        ar.Values[AggregateOp.Max]["createdAt"].Should().BeOfType<DateTime>();
    }
}
