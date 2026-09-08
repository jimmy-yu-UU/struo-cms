// tests/Struo.Tests/Query/FacetQueriesTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class FacetQueriesTests : IDisposable
{
    private readonly SubqueryPushdownHarness _h = new();
    public void Dispose() { _h.Dispose(); GC.SuppressFinalize(this); }

    private static ComparisonFilter Eq(string p, object v) => new(p, QueryOperator.Eq, v);
    private static (object? Value, long Count)[] Pairs(IReadOnlyList<FacetBucket> b) => b.Select(x => (x.Value, x.Count)).ToArray();

    [Fact]
    public async Task Own_field_facet_counts_rows_and_orders_by_count_then_value()
    {
        _h.SeedEav();
        var b = await _h.FacetAsync("name");
        Pairs(b).Should().Equal(("bare", 1), ("cross", 1), ("nocat", 1), ("same", 1));
    }

    [Fact]
    public async Task Foreign_key_facet_has_a_null_bucket_and_id_strings()
    {
        _h.SeedEav();
        var tech = _h.Db.Queryable<SqCategory>().First(c => c.Name == "Tech").Id.ToString();
        var archive = _h.Db.Queryable<SqCategory>().First(c => c.Name == "Archive").Id.ToString();
        var b = await _h.FacetAsync("categoryId");
        b[0].Should().Be(new FacetBucket(tech, 2));
        b.Skip(1).Should().BeEquivalentTo([new FacetBucket(archive, 1), new FacetBucket(null, 1)]);
    }

    [Fact]
    public async Task Many_to_one_relation_facet_drops_the_null_bucket()
    {
        _h.SeedEav();
        var b = await _h.FacetAsync("category");
        b.Should().HaveCount(2);
        b.Select(x => x.Count).Should().Equal(2, 1);
        b.Should().NotContain(x => x.Value == null);
    }

    [Fact]
    public async Task One_to_many_relation_facet_groups_by_child_id()
    {
        _h.SeedEav();
        var b = await _h.FacetAsync("properties");
        b.Should().HaveCount(4);
        b.Should().OnlyContain(x => x.Count == 1 && x.Value is string);
    }

    [Fact]
    public async Task Many_to_many_relation_facet_counts_distinct_roots()
    {
        _h.SeedEav();
        var guide = _h.Db.Queryable<SqLabel>().First(l => l.Name == "Guide").Id.ToString();
        var misc = _h.Db.Queryable<SqLabel>().First(l => l.Name == "Misc").Id.ToString();
        var b = await _h.FacetAsync("labels");
        Pairs(b).Should().Equal((guide, 2), (misc, 1));
    }

    [Fact]
    public async Task One_to_many_leaf_facet_counts_roots_with_at_least_one_matching_child()
    {
        _h.SeedEav();
        var b = await _h.FacetAsync("properties.code");
        b[0].Should().Be(new FacetBucket("vds-v", 2));
        b.Skip(1).Should().BeEquivalentTo([new FacetBucket("o'neil", 1), new FacetBucket("ptot-w", 1)]);
        b.Skip(1).Select(x => (string)x.Value!).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public async Task Many_to_one_leaf_facet_swaps_ids_for_leaf_values()
    {
        _h.SeedEav();
        Pairs(await _h.FacetAsync("category.name")).Should().Equal(("Tech", 2), ("Archive", 1));
    }

    [Fact]
    public async Task Many_to_many_leaf_facet_swaps_ids_for_leaf_values()
    {
        _h.SeedEav();
        Pairs(await _h.FacetAsync("labels.name")).Should().Equal(("Guide", 2), ("Misc", 1));
    }

    [Fact]
    public async Task Root_filter_narrows_the_relation_facet()
    {
        _h.SeedEav();
        var tech = _h.Db.Queryable<SqCategory>().First(c => c.Name == "Tech").Id;
        var b = await _h.FacetAsync("labels.name", Eq("categoryId", tech.ToString()));
        Pairs(b).Should().Equal(("Guide", 1));
    }

    [Fact]
    public async Task Root_search_applies_to_the_facet()
    {
        _h.SeedEav();
        var b = await _h.FacetAsync("category.name", search: "cross");
        Pairs(b).Should().Equal(("Tech", 1));
    }

    [Fact]
    public async Task Max_values_limits_buckets()
    {
        _h.SeedEav();
        (await _h.FacetAsync("name", maxValues: 2)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Leaf_facet_merges_duplicate_leaf_values_and_re_sorts()
    {
        _h.SeedEav();
        var dup = new SqCategory { Id = Guid.NewGuid(), Name = "Tech" };
        _h.Db.Insertable(dup).ExecuteCommand();
        _h.Db.Insertable(new SqProduct { Id = Guid.NewGuid(), Name = "extra", CategoryId = dup.Id }).ExecuteCommand();
        Pairs(await _h.FacetAsync("category.name")).Should().Equal(("Tech", 3), ("Archive", 1));
    }

    [Fact]
    public async Task Soft_deleted_roots_follow_the_deleted_mode()
    {
        var (_, _, bare, _) = _h.SeedEav();
        await _h.Repo.SoftDeleteAsync("sqProduct", bare.ToString(), DateTime.UtcNow, null);
        // Excluding the soft-deleted row leaves Tech and Archive tied at count 1 each. The leaf-value
        // merge/resort (SwapLeafValues) breaks count ties by ascending leaf value ("Archive" < "Tech"
        // ordinally) — the same rule Leaf_facet_merges_duplicate_leaf_values_and_re_sorts and
        // One_to_many_leaf_facet_counts_roots_with_at_least_one_matching_child pin down elsewhere — so
        // this assertion checks the set, not a specific tie order, while the two non-tied assertions
        // below keep their strict order.
        Pairs(await _h.FacetAsync("category.name")).Should().BeEquivalentTo([("Tech", 1L), ("Archive", 1L)]);
        Pairs(await _h.FacetAsync("category.name", deleted: DeletedFilter.With)).Should().Equal(("Tech", 2), ("Archive", 1));
        Pairs(await _h.FacetAsync("category.name", deleted: DeletedFilter.Only)).Should().Equal(("Tech", 1));
    }

    [Fact]
    public async Task Own_facet_is_one_statement_and_leaf_facet_is_two()
    {
        _h.SeedEav();
        _h.ResetSqlCount();
        await _h.FacetAsync("name");
        _h.SqlStatements.Should().Be(1);
        _h.ResetSqlCount();
        await _h.FacetAsync("labels.name");
        _h.SqlStatements.Should().Be(2);
    }

    [Fact]
    public async Task Empty_result_set_yields_no_buckets()
    {
        _h.SeedEav();
        (await _h.FacetAsync("labels", Eq("name", "does-not-exist"))).Should().BeEmpty();
    }

    [Fact]
    public async Task Soft_deleted_relation_target_is_dropped_from_the_id_bucket()
    {
        var (crossRow, _, _, _) = _h.SeedEav();
        var (live, trashed) = _h.SeedSoftLabels(crossRow);
        await _h.Repo.SoftDeleteAsync("sqSoftLabel", trashed.ToString(), DateTime.UtcNow, null);

        var b = await _h.FacetAsync("softLabels");

        b.Select(x => x.Value).Should().Equal(live.ToString());
    }

    [Fact]
    public async Task Soft_deleted_relation_target_is_dropped_from_the_leaf_bucket()
    {
        var (crossRow, _, _, _) = _h.SeedEav();
        var (_, trashed) = _h.SeedSoftLabels(crossRow);
        await _h.Repo.SoftDeleteAsync("sqSoftLabel", trashed.ToString(), DateTime.UtcNow, null);

        var b = await _h.FacetAsync("softLabels.name");

        b.Select(x => x.Value).Should().Equal("Alive");
    }

    [Fact]
    public async Task A_deleted_with_root_query_still_drops_the_trashed_target_from_the_relation_facet()
    {
        // The related side never lifts the soft-delete filter, regardless of the outer request's
        // `deleted=` mode — same contract as the existing many-to-one leaf case
        // (Soft_deleted_roots_follow_the_deleted_mode) exercises for the ROOT side.
        var (crossRow, _, _, _) = _h.SeedEav();
        var (live, trashed) = _h.SeedSoftLabels(crossRow);
        await _h.Repo.SoftDeleteAsync("sqSoftLabel", trashed.ToString(), DateTime.UtcNow, null);

        var b = await _h.FacetAsync("softLabels", deleted: DeletedFilter.With);

        b.Select(x => x.Value).Should().Equal(live.ToString());
    }
}

// A second facet fixture — over the real sample Article/Category graph — pins down the translatable-leaf
// path: Article.title lives on the ArticleTranslation sidecar, so `articles.title` (a one-to-many hop
// from Category) must resolve per-locale sidecar values rather than the target row's own scalar field.
public class TranslatableLeafFacetTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly SqlSugarItemRepository _repo;
    private readonly CachedMetadataProvider _md;
    private readonly RelationshipGraph _graph;

    public TranslatableLeafFacetTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<Category>(); _db.CodeFirst.InitTables<Article>(); _db.CodeFirst.InitTables<ArticleTranslation>();
        var types = new[] { typeof(Article), typeof(Category), typeof(Tag) };
        var collections = MetadataScanner.ScanTypes(types);
        _md = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        _graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        { ["article"] = typeof(Article), ["category"] = typeof(Category), ["tag"] = typeof(Tag) });
        _repo = new SqlSugarItemRepository(_db, registry, _graph, _md, new StruoQueryOptions());
    }

    public void Dispose() { _file.Dispose(); GC.SuppressFinalize(this); }

    [Fact]
    public async Task Translatable_leaf_uses_the_query_locale_and_puts_untranslated_targets_in_the_null_bucket()
    {
        var cat = new Category { Id = Guid.NewGuid(), Name = "C" };
        _db.Insertable(cat).ExecuteCommand();
        var a1 = new Article { Id = Guid.NewGuid(), Status = "published", CategoryId = cat.Id };
        var a2 = new Article { Id = Guid.NewGuid(), Status = "published", CategoryId = cat.Id };
        _db.Insertable(new[] { a1, a2 }).ExecuteCommand();
        _db.Insertable(new[]
        {
            new ArticleTranslation { ArticleId = a1.Id, Locale = "en", Title = "Alpha" },
            new ArticleTranslation { ArticleId = a1.Id, Locale = "zh-TW", Title = "甲" },
            new ArticleTranslation { ArticleId = a2.Id, Locale = "en", Title = "Beta" },
        }).ExecuteCommand();

        var meta = _md.GetCollection("category")!;
        var facet = FacetPathResolver.Resolve(meta, "articles.title", _graph, _md);
        var q = new QueryModel(null, null, [], 100, 0, null);

        var en = await _repo.FacetAsync(new FacetRequest("category", q, facet, [], "en", DeletedFilter.Exclude, 50));
        en.Select(b => (b.Value, b.Count)).Should().Equal(("Alpha", 1), ("Beta", 1));

        var zh = await _repo.FacetAsync(new FacetRequest("category", q, facet, [], "zh-TW", DeletedFilter.Exclude, 50));
        zh.Should().BeEquivalentTo([new FacetBucket("甲", 1), new FacetBucket(null, 1)]);
    }

    [Fact]
    public async Task Translatable_leaf_without_a_query_locale_throws()
    {
        var cat = new Category { Id = Guid.NewGuid(), Name = "C" };
        _db.Insertable(cat).ExecuteCommand();
        var a1 = new Article { Id = Guid.NewGuid(), Status = "published", CategoryId = cat.Id };
        _db.Insertable(a1).ExecuteCommand();
        _db.Insertable(new ArticleTranslation { ArticleId = a1.Id, Locale = "en", Title = "Alpha" }).ExecuteCommand();

        var meta = _md.GetCollection("category")!;
        var facet = FacetPathResolver.Resolve(meta, "articles.title", _graph, _md);
        var q = new QueryModel(null, null, [], 100, 0, null);

        Func<Task> act = async () => await _repo.FacetAsync(new FacetRequest("category", q, facet, [], null, DeletedFilter.Exclude, 50));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*query locale is required*");
    }
}
