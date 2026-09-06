// tests/Struo.Tests/Query/FacetSqlShapeTests.cs
using System.Linq.Expressions;
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

public class FacetSqlShapeTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;

    public FacetSqlShapeTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<Category>();
        _db.CodeFirst.InitTables<ArticleTag>();
    }

    public void Dispose() { _file.Dispose(); GC.SuppressFinalize(this); }

    private static ISugarQueryable<FacetRow<TValue>> Facet<T, TValue>(ISugarQueryable<T> q, string valueProp, string countProp, bool distinct, int take) where T : class, new()
    {
        var key = (Expression<Func<T, object>>)ColumnSelectorFactory.BoxedSelector(typeof(T), valueProp);
        var count = (Expression<Func<T, object>>)ColumnSelectorFactory.CountSelector(typeof(T), countProp, distinct);
        var proj = (Expression<Func<T, FacetRow<TValue>>>)ColumnSelectorFactory.FacetProjection(typeof(T), valueProp, countProp, distinct);
        return q.GroupBy(key).OrderBy(count, OrderByType.Desc).OrderBy(key).Select(proj).Take(take);
    }

    [Fact]
    public void Own_nullable_column_facet_groups_counts_orders_and_limits_with_typed_api_only()
    {
        var a = Guid.NewGuid();
        _db.Insertable(new[]
        {
            new Category { Id = a, Name = "A" }, new Category { Id = Guid.NewGuid(), Name = "B", ParentId = a },
            new Category { Id = Guid.NewGuid(), Name = "C", ParentId = a }, new Category { Id = Guid.NewGuid(), Name = "D" },
        }).ExecuteCommand();

        var q = Facet<Category, Guid?>(_db.Queryable<Category>(), "ParentId", "Id", distinct: false, take: 10);
        var sql = q.ToSql().Key;
        sql.Should().MatchRegex(@"GROUP BY\s+`ParentId`");
        sql.Should().MatchRegex(@"COUNT\(`Id`\)\s+AS\s+`Count`");
        sql.Should().MatchRegex(@"ORDER BY\s+COUNT\(`Id`\)\s+DESC,\s*`ParentId`\s+ASC");
        sql.Should().NotContain("SqlSugar.");
        var rows = q.ToList();
        rows.Should().HaveCount(2);
        rows.Should().Contain(r => r.Value == null && r.Count == 2);
        rows.Should().Contain(r => r.Value == a && r.Count == 2);
    }

    [Fact]
    public void Distinct_count_facet_renders_count_distinct_in_select_and_order_by()
    {
        var a1 = Guid.NewGuid(); var a2 = Guid.NewGuid(); var t1 = Guid.NewGuid(); var t2 = Guid.NewGuid();
        _db.Insertable(new[]
        {
            new ArticleTag { Id = Guid.NewGuid(), ArticleId = a1, TagId = t1, Sort = 1 },
            new ArticleTag { Id = Guid.NewGuid(), ArticleId = a1, TagId = t2, Sort = 2 },
            new ArticleTag { Id = Guid.NewGuid(), ArticleId = a2, TagId = t1, Sort = 3 },
        }).ExecuteCommand();

        var q = Facet<ArticleTag, Guid>(_db.Queryable<ArticleTag>(), "TagId", "ArticleId", distinct: true, take: 10);
        q.ToSql().Key.Should().MatchRegex(@"COUNT\(DISTINCT\s+`ArticleId`\s*\)\s+AS\s+`Count`");
        var rows = q.ToList();
        rows[0].Value.Should().Be(t1); rows[0].Count.Should().Be(2);
        rows[1].Value.Should().Be(t2); rows[1].Count.Should().Be(1);
    }

    [Fact]
    public void Take_limits_the_number_of_buckets()
    {
        _db.Insertable(new[] { new Category { Id = Guid.NewGuid(), Name = "A" }, new Category { Id = Guid.NewGuid(), Name = "B" }, new Category { Id = Guid.NewGuid(), Name = "C" } }).ExecuteCommand();
        var rows = Facet<Category, string>(_db.Queryable<Category>(), "Name", "Id", false, take: 2).ToList();
        rows.Select(r => r.Value).Should().Equal("A", "B");
    }

    [Fact]
    public void Facet_composes_with_a_subquery_wrapped_root_condition()
    {
        var a = Guid.NewGuid();
        _db.Insertable(new[] { new Category { Id = a, Name = "A" }, new Category { Id = Guid.NewGuid(), Name = "B", ParentId = a }, new Category { Id = Guid.NewGuid(), Name = "D" } }).ExecuteCommand();
        var sub = _db.Queryable<Category>()
            .Where(new List<IConditionalModel> { new ConditionalModel { FieldName = "Name", ConditionalType = ConditionalType.Equal, FieldValue = "A" } })
            .Select((Expression<Func<Category, Guid>>)ColumnSelectorFactory.TypedSelector(typeof(Category), "Id")).ToSql();
        var wrapped = SubQueryConditional.Wrap(SubQueryKind.In, _db.EntityMaintenance.GetDbColumnName<Category>("ParentId"), sub);
        var q = Facet<Category, string>(_db.Queryable<Category>().Where(new List<IConditionalModel> { wrapped }), "Name", "Id", false, 10);
        q.ToSql().Key.Should().MatchRegex(@"IN \(SELECT `Id` FROM `categories`");
        q.ToList().Select(r => r.Value).Should().Equal("B");
    }

    [Fact]
    public void Aggregate_projection_renders_one_function_per_slot()
    {
        _db.Insertable(new[]
        {
            new ArticleTag { Id = Guid.NewGuid(), ArticleId = Guid.NewGuid(), TagId = Guid.NewGuid(), Sort = 1, Note = "x" },
            new ArticleTag { Id = Guid.NewGuid(), ArticleId = Guid.NewGuid(), TagId = Guid.NewGuid(), Sort = 4, Note = null },
        }).ExecuteCommand();
        var proj = (Expression<Func<ArticleTag, AggregateRow>>)ColumnSelectorFactory.AggregateProjection(typeof(ArticleTag),
            [(AggregateOp.Sum, "Sort"), (AggregateOp.Min, "Sort"), (AggregateOp.Max, "Sort"), (AggregateOp.Avg, "Sort"), (AggregateOp.Count, "Note")]);
        var q = _db.Queryable<ArticleTag>().Select(proj);
        var sql = q.ToSql().Key;
        sql.Should().MatchRegex(@"SUM\(`Sort`\)\s+AS\s+`V0`");
        sql.Should().MatchRegex(@"MIN\(`Sort`\)\s+AS\s+`V1`");
        sql.Should().MatchRegex(@"MAX\(`Sort`\)\s+AS\s+`V2`");
        sql.Should().MatchRegex(@"AVG\(`Sort`\)\s+AS\s+`V3`");
        sql.Should().MatchRegex(@"COUNT\(`Note`\)\s+AS\s+`V4`");
        var r = q.First()!;
        Convert.ToInt64(r.V0).Should().Be(5);
        Convert.ToInt64(r.V4).Should().Be(1);
        Convert.ToDouble(r.V3).Should().Be(2.5);
    }

    [Fact]
    public void Aggregate_over_an_empty_set_returns_nulls_and_zero_count()
    {
        var proj = (Expression<Func<ArticleTag, AggregateRow>>)ColumnSelectorFactory.AggregateProjection(typeof(ArticleTag),
            [(AggregateOp.Sum, "Sort"), (AggregateOp.Count, "Note")]);
        var r = _db.Queryable<ArticleTag>().Select(proj).First()!;
        r.V0.Should().BeNull();
        Convert.ToInt64(r.V1).Should().Be(0);
    }

    [Fact]
    public void Aggregate_projection_rejects_more_than_ten_slots()
    {
        var slots = Enumerable.Range(0, 11).Select(_ => (AggregateOp.Count, "Sort")).ToList();
        var act = () => ColumnSelectorFactory.AggregateProjection(typeof(ArticleTag), slots);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // Category is [SugarTable("categories")] with no per-column [SugarColumn(Name=...)] override, so
    // SqlSugar's default SELECT lists every mapped property by its own name — including the literal
    // column "DeletedAt" — regardless of DeletedFilter. That means "does the rendered SQL contain the
    // substring DeletedAt" can never distinguish the three scopes; the actual scope-defining signal is
    // whether a DeletedAt IS [NOT] NULL *guard* appears in the WHERE clause, so the with/only
    // assertions below match that guard shape specifically rather than the bare column name.
    [Fact]
    public void DeletedScope_only_adds_the_deleted_at_guard_and_lifts_the_global_filter()
    {
        var exclude = DeletedScope.Root<Category>(_db, [], DeletedFilter.Exclude).ToSql().Key;
        var with = DeletedScope.Root<Category>(_db, [], DeletedFilter.With).ToSql().Key;
        var only = DeletedScope.Root<Category>(_db, [], DeletedFilter.Only).ToSql().Key;
        exclude.Should().MatchRegex(@"`DeletedAt`\s+IS\s+NULL");
        with.Should().NotMatchRegex(@"`DeletedAt`\s+IS\s+(NOT\s+)?NULL");
        only.Should().MatchRegex(@"`DeletedAt`\s+IS\s+NOT\s+NULL");
        only.Should().NotMatchRegex(@"`DeletedAt`\s+IS\s+NULL\b(?!\s*OR)");
    }
}
