// tests/Struo.Tests/Query/FilterTranslatorSqlShapeTests.cs
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Query;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class FilterTranslatorSqlShapeTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;

    public FilterTranslatorSqlShapeTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<Category>();
        _db.CodeFirst.InitTables<Article>();
    }

    public void Dispose() => _file.Dispose();

    // ConditionalType.Equal, deliberately: this is the shape ConditionalModelTranslator actually
    // produces for the leaf conditionals relation-filter pushdown will nest (Eq/GreaterThan/Like/...
    // are all parameterized), so the subquery this builds carries a real SqlSugar-assigned parameter
    // name (e.g. "@ConditName0") — the exact case SubQueryConditional's renaming exists to handle.
    private KeyValuePair<string, List<SugarParameter>> CategoryIdsNamed(string name)
    {
        var sel = (Expression<Func<Category, Guid>>)ColumnSelectorFactory.TypedSelector(typeof(Category), "Id");
        return _db.Queryable<Category>()
            .Where(new List<IConditionalModel> { new ConditionalModel { FieldName = "Name", ConditionalType = ConditionalType.Equal, FieldValue = name } })
            .Select(sel).ToSql();
    }

    [Fact]
    public void Wrapped_subquery_parameters_are_renamed_with_a_unique_prefix()
    {
        var sub = CategoryIdsNamed("Tech");
        var originalNames = sub.Value.Select(p => p.ParameterName).ToList();
        originalNames.Should().NotBeEmpty("Equal parameterizes its FieldValue, unlike In's literal inlining");

        var col = _db.EntityMaintenance.GetDbColumnName("CategoryId", typeof(Article));
        var cond = SubQueryConditional.Wrap(SubQueryKind.In, col, sub);
        var wrapped = _db.Queryable<Article>().Where(new List<IConditionalModel> { cond }).ToSql();

        wrapped.Key.Should().NotContain("SqlSugar.");
        wrapped.Value.Should().NotBeEmpty();
        foreach (var parameter in wrapped.Value)
        {
            parameter.ParameterName.Should().MatchRegex(@"^@sq\d+_");
            wrapped.Key.Should().Contain(parameter.ParameterName);
        }
        foreach (var original in originalNames)
            wrapped.Key.Should().NotContain(original);
    }

    [Fact]
    public void Object_selector_compiles_to_Func_of_object()
    {
        var lambda = ColumnSelectorFactory.ObjectSelector(typeof(Article), "CategoryId");
        lambda.Should().BeAssignableTo<Expression<Func<Article, object>>>();
    }

    [Fact]
    public void Wrapped_subquery_works_at_the_top_level_of_a_where_list()
    {
        var tech = new Category { Id = Guid.NewGuid(), Name = "Tech" };
        var news = new Category { Id = Guid.NewGuid(), Name = "News" };
        _db.Insertable(new[] { tech, news }).ExecuteCommand();
        _db.Insertable(new[]
        {
            new Article { Id = Guid.NewGuid(), Status = "draft", CategoryId = tech.Id },
            new Article { Id = Guid.NewGuid(), Status = "draft", CategoryId = news.Id },
        }).ExecuteCommand();

        var col = _db.EntityMaintenance.GetDbColumnName("CategoryId", typeof(Article));
        var cond = SubQueryConditional.Wrap(SubQueryKind.In, col, CategoryIdsNamed("Tech"));
        var q = _db.Queryable<Article>().Where(new List<IConditionalModel> { cond });

        q.ToSql().Key.Should().Contain($"{col} IN (SELECT");
        q.ToList().Should().ContainSingle().Which.CategoryId.Should().Be(tech.Id);
    }

    [Fact]
    public void Wrapped_subquery_inside_an_or_group_keeps_its_parentheses()
    {
        var col = _db.EntityMaintenance.GetDbColumnName("CategoryId", typeof(Article));
        var group = new ConditionalCollections
        {
            ConditionalList =
            [
                new(WhereType.Or, new ConditionalModel { FieldName = "Status", ConditionalType = ConditionalType.Equal, FieldValue = "draft" }),
                new(WhereType.Or, SubQueryConditional.Wrap(SubQueryKind.In, col, CategoryIdsNamed("Tech"))),
            ]
        };
        var sql = _db.Queryable<Article>().Where(new List<IConditionalModel> { group }).ToSql().Key;
        sql.Should().MatchRegex(@"\(\s*.*Status.*OR\s+.*IN \(SELECT.*\)\s*\)");
    }

    [Fact]
    public void NullOrNotIn_renders_the_null_guard()
    {
        var col = _db.EntityMaintenance.GetDbColumnName("CategoryId", typeof(Article));
        var cond = SubQueryConditional.Wrap(SubQueryKind.NullOrNotIn, col, CategoryIdsNamed("Tech"));
        var sql = _db.Queryable<Article>().Where(new List<IConditionalModel> { cond }).ToSql().Key;
        sql.Should().Contain($"({col} IS NULL OR {col} NOT IN (SELECT");
    }

    [Fact]
    public void Literal_with_apostrophe_is_escaped_inside_the_subquery()
    {
        var cat = new Category { Id = Guid.NewGuid(), Name = "O'Brien" };
        _db.Insertable(cat).ExecuteCommand();
        _db.Insertable(new Article { Id = Guid.NewGuid(), Status = "draft", CategoryId = cat.Id }).ExecuteCommand();

        var col = _db.EntityMaintenance.GetDbColumnName("CategoryId", typeof(Article));
        var cond = SubQueryConditional.Wrap(SubQueryKind.In, col, CategoryIdsNamed("O'Brien"));
        var rows = _db.Queryable<Article>().Where(new List<IConditionalModel> { cond }).ToList();
        rows.Should().ContainSingle();
    }

    // Reproduces the exact collision the fix targets: an outer ConditionalModel and an inner (wrapped)
    // subquery both filter Category.Name at conditional-list index 0, in ONE plain top-level list —
    // NOT inside a ConditionalCollections OR group, whose members get a 1000-offset index
    // ("@ConditStatus1000") and so never actually collide with a plain list's "@ConditName0". The
    // premise is enforced, not just commented: the outer leaf's OWN generated parameter name — built
    // standalone, on a throwaway queryable, exactly as it will be built inside the real combined list
    // below — is asserted equal to the inner subquery's pre-rename parameter name.
    [Fact]
    public void Outer_and_inner_parameters_with_the_same_generated_name_do_not_collide()
    {
        var tech = new Category { Id = Guid.NewGuid(), Name = "Tech" };
        var news = new Category { Id = Guid.NewGuid(), Name = "News" };
        _db.Insertable(new[] { tech, news }).ExecuteCommand();

        var idCol = _db.EntityMaintenance.GetDbColumnName("Id", typeof(Category));

        ConditionalModel OuterNameLeaf(string name) => new() { FieldName = "Name", ConditionalType = ConditionalType.Equal, FieldValue = name };

        // Enforce the premise: the outer leaf, built standalone as the sole/first entry of a plain
        // top-level list, gets the SAME generated parameter name SqlSugar assigns to the inner
        // subquery's own (also sole/first, also plain-top-level-list) filter.
        var outerStandalone = _db.Queryable<Category>().Where(new List<IConditionalModel> { OuterNameLeaf("Tech") }).ToSql();
        var innerSubTech = CategoryIdsNamed("Tech");
        outerStandalone.Value.Select(p => p.ParameterName).Should().BeEquivalentTo(
            innerSubTech.Value.Select(p => p.ParameterName),
            "the outer leaf and the inner subquery are each index-0 of their own plain top-level list, so SqlSugar assigns them the same generated name before renaming");

        // Positive: Category.Name = 'Tech' AND Id IN (SELECT Id FROM categories WHERE Name = 'Tech') -> Tech only.
        var condTech = SubQueryConditional.Wrap(SubQueryKind.In, idCol, innerSubTech);
        var rowsPositive = _db.Queryable<Category>().Where(new List<IConditionalModel> { OuterNameLeaf("Tech"), condTech }).ToList();
        rowsPositive.Should().ContainSingle().Which.Name.Should().Be("Tech");

        // Negative: same outer leaf, but the inner subquery now filters 'News' instead. If the outer
        // value ('Tech') had clobbered the inner one (the actual collision symptom), this would still
        // spuriously match Tech; it must instead return ZERO rows.
        var condNews = SubQueryConditional.Wrap(SubQueryKind.In, idCol, CategoryIdsNamed("News"));
        var rowsNegative = _db.Queryable<Category>().Where(new List<IConditionalModel> { OuterNameLeaf("Tech"), condNews }).ToList();
        rowsNegative.Should().BeEmpty();
    }

    // A second, independent way the same collision could reappear: passing the SAME `sub`
    // KeyValuePair to Wrap() twice. Proves Renamed() copies rather than mutates — the shared input
    // parameters are untouched, and each wrapped conditional's SQL/parameters stay self-consistent
    // (no cross-contamination), so both execute correctly even reading from the exact same `sub`.
    [Fact]
    public void Wrapping_the_same_subquery_twice_does_not_corrupt_either_copy()
    {
        var tech = new Category { Id = Guid.NewGuid(), Name = "Tech" };
        _db.Insertable(tech).ExecuteCommand();
        _db.Insertable(new[]
        {
            new Article { Id = Guid.NewGuid(), Status = "draft", CategoryId = tech.Id },
            new Article { Id = Guid.NewGuid(), Status = "published", CategoryId = tech.Id },
        }).ExecuteCommand();

        var col = _db.EntityMaintenance.GetDbColumnName("CategoryId", typeof(Article));
        var sub = CategoryIdsNamed("Tech");
        var originalNames = sub.Value.Select(p => p.ParameterName).ToList();

        var condDraft = SubQueryConditional.Wrap(SubQueryKind.In, col, sub);
        var condPublished = SubQueryConditional.Wrap(SubQueryKind.In, col, sub);

        // The shared input list must be untouched by either Wrap() call.
        sub.Value.Select(p => p.ParameterName).Should().Equal(originalNames);

        var qDraft = _db.Queryable<Article>().Where(new List<IConditionalModel>
        {
            new ConditionalModel { FieldName = "Status", ConditionalType = ConditionalType.Equal, FieldValue = "draft" },
            condDraft,
        });
        var qPublished = _db.Queryable<Article>().Where(new List<IConditionalModel>
        {
            new ConditionalModel { FieldName = "Status", ConditionalType = ConditionalType.Equal, FieldValue = "published" },
            condPublished,
        });

        var sqlDraft = qDraft.ToSql();
        var sqlPublished = qPublished.ToSql();

        // Each instance's SQL references exactly its own parameter names — no desync, no cross-read.
        foreach (var p in sqlDraft.Value) sqlDraft.Key.Should().Contain(p.ParameterName);
        foreach (var p in sqlPublished.Value) sqlPublished.Key.Should().Contain(p.ParameterName);

        qDraft.ToList().Should().ContainSingle().Which.Status.Should().Be("draft");
        qPublished.ToList().Should().ContainSingle().Which.Status.Should().Be("published");
    }

    // Two independent Wrap() calls, each nesting the previous level's ToSql() output: level 2 (a
    // plain Category-by-Name filter) is wrapped into level 1's own Where list (also filtering
    // Category.Name — same field/index as level 2's internal filter), and level 1's ToSql() is then
    // wrapped again for the Article-level outer query. Proves composition through Wrap works at
    // arbitrary nesting depth, and that each level gets its own unique parameter prefix.
    [Fact]
    public void Two_nesting_levels_compose_through_Wrap()
    {
        var tech = new Category { Id = Guid.NewGuid(), Name = "Tech" };
        var news = new Category { Id = Guid.NewGuid(), Name = "News" };
        _db.Insertable(new[] { tech, news }).ExecuteCommand();
        _db.Insertable(new[]
        {
            new Article { Id = Guid.NewGuid(), Status = "draft", CategoryId = tech.Id },
            new Article { Id = Guid.NewGuid(), Status = "draft", CategoryId = news.Id },
        }).ExecuteCommand();

        var idCol = _db.EntityMaintenance.GetDbColumnName("Id", typeof(Category));
        var categoryIdCol = _db.EntityMaintenance.GetDbColumnName("CategoryId", typeof(Article));

        // Level 2 (innermost): SELECT Id FROM categories WHERE Name = 'Tech'
        var level2Wrapped = SubQueryConditional.Wrap(SubQueryKind.In, idCol, CategoryIdsNamed("Tech"));

        // Level 1 (middle): SELECT Id FROM categories WHERE Name = 'Tech' AND Id IN (level 2)
        var sel = (Expression<Func<Category, Guid>>)ColumnSelectorFactory.TypedSelector(typeof(Category), "Id");
        var level1 = _db.Queryable<Category>()
            .Where(new List<IConditionalModel>
            {
                new ConditionalModel { FieldName = "Name", ConditionalType = ConditionalType.Equal, FieldValue = "Tech" },
                level2Wrapped,
            })
            .Select(sel).ToSql();
        var level1Wrapped = SubQueryConditional.Wrap(SubQueryKind.In, categoryIdCol, level1);

        // Outer: Article.Status = 'draft' AND Article.CategoryId IN (level 1)
        var q = _db.Queryable<Article>().Where(new List<IConditionalModel>
        {
            new ConditionalModel { FieldName = "Status", ConditionalType = ConditionalType.Equal, FieldValue = "draft" },
            level1Wrapped,
        });
        var final = q.ToSql();

        // Nested renaming keeps the inner prefix as a body substring (only the outermost "sqN_" keeps
        // its leading "@" — e.g. "@sq2_sq1_ConditName0"), so match "sqN_" without requiring "@" before it.
        var prefixes = Regex.Matches(final.Key, @"sq(\d+)_").Select(m => m.Groups[1].Value).Distinct().ToList();
        prefixes.Should().HaveCount(2, "two independent Wrap() calls (level2->level1, level1->outer) each allocate their own prefix");

        q.ToList().Should().ContainSingle().Which.CategoryId.Should().Be(tech.Id);
    }

    [Fact]
    public void None_on_o2m_adds_the_is_not_null_guard_on_the_projected_fk()
    {
        using var h = new SubqueryPushdownHarness();
        var t = new FilterTranslator(h.Db, h.Graph, h.Metadata, h.Registry, h.Options);
        var conds = t.Translate("sqProduct", new RelationPredicateFilter("properties", RelationQuantifier.None, new ComparisonFilter("code", QueryOperator.Eq, "x")), null, [], null);
        var sql = h.Db.Queryable<SqProduct>().Where(conds).ToSql().Key;
        // SqlSugar emits irregular internal whitespace around IS NOT NULL (e.g. "IS NOT  NULL" with a
        // doubled space) — match with whitespace tolerance rather than a literal substring.
        sql.Should().Contain("NOT IN (SELECT").And.MatchRegex(@"IS\s+NOT\s+NULL");
    }

    [Fact]
    public void M2m_predicate_nests_target_subquery_inside_junction_subquery()
    {
        using var h = new SubqueryPushdownHarness();
        var t = new FilterTranslator(h.Db, h.Graph, h.Metadata, h.Registry, h.Options);
        var conds = t.Translate("sqProduct", new ComparisonFilter("labels.name", QueryOperator.Eq, "Guide"), null, [], null);
        var sql = h.Db.Queryable<SqProduct>().Where(conds).ToSql().Key;
        sql.Should().MatchRegex(@"IN \(SELECT [\s\S]*sq_product_labels[\s\S]* IN \(SELECT [\s\S]*sq_labels");
        sql.Should().NotContain("SqlSugar.");
    }
}
