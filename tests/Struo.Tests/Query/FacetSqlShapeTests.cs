// tests/Struo.Tests/Query/FacetSqlShapeTests.cs
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

public class FacetSqlShapeTests : IDisposable
{
    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;

    public FacetSqlShapeTests()
    {
        _db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<Category>();
        _db.CodeFirst.InitTables<ArticleTag>();
    }

    public void Dispose() { _file.Dispose(); GC.SuppressFinalize(this); }

    // Regex fragments for identifiers quoted the way the selected backend's SQL builder quotes them.
    private string Col<T>(string property) where T : class, new() =>
        Quoted<T>(_db.EntityMaintenance.GetDbColumnName<T>(property));

    private string Alias(string name) => Quoted<Category>(name);

    private string Table<T>() where T : class, new() =>
        Regex.Escape(_db.Queryable<T>().QueryBuilder.Builder.GetTranslationTableName(_db.EntityMaintenance.GetTableName<T>()));

    // The repository casts integral AVG operands to float on SQL Server (AggregateQueries); the projection here does the same.
    private bool FloatAverageOfIntegers => _db.CurrentConnectionConfig.DbType == DbType.SqlServer;

    private string Quoted<T>(string name) where T : class, new() =>
        Regex.Escape(_db.Queryable<T>().QueryBuilder.Builder.GetTranslationColumnName(name));

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
        var parentId = Col<Category>("ParentId");
        var id = Col<Category>("Id");
        sql.Should().MatchRegex($@"GROUP BY\s+{parentId}");
        sql.Should().MatchRegex($@"COUNT\({id}\)\s+AS\s+{Alias("Count")}");
        sql.Should().MatchRegex($@"ORDER BY\s+COUNT\({id}\)\s+DESC,\s*{parentId}\s+ASC");
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
        q.ToSql().Key.Should().MatchRegex($@"COUNT\(DISTINCT\s+{Col<ArticleTag>("ArticleId")}\s*\)\s+AS\s+{Alias("Count")}");
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
        q.ToSql().Key.Should().MatchRegex($@"IN \(SELECT {Col<Category>("Id")} FROM {Table<Category>()}");
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
            [(AggregateOp.Sum, "Sort"), (AggregateOp.Min, "Sort"), (AggregateOp.Max, "Sort"), (AggregateOp.Avg, "Sort"), (AggregateOp.Count, "Note")],
            FloatAverageOfIntegers);
        var q = _db.Queryable<ArticleTag>().Select(proj);
        var sql = q.ToSql().Key;
        var sort = Col<ArticleTag>("Sort");
        sql.Should().MatchRegex($@"SUM\({sort}\)\s+AS\s+{Alias("V0")}");
        sql.Should().MatchRegex($@"MIN\({sort}\)\s+AS\s+{Alias("V1")}");
        sql.Should().MatchRegex($@"MAX\({sort}\)\s+AS\s+{Alias("V2")}");
        var averaged = FloatAverageOfIntegers ? $@"\s*CAST\({sort} AS FLOAT\)\s*" : sort;
        sql.Should().MatchRegex($@"AVG\({averaged}\)\s+AS\s+{Alias("V3")}");
        sql.Should().MatchRegex($@"COUNT\({Col<ArticleTag>("Note")}\)\s+AS\s+{Alias("V4")}");
        var r = q.First()!;
        Convert.ToInt64(r.V0).Should().Be(5);
        Convert.ToInt64(r.V4).Should().Be(1);
        Convert.ToDouble(r.V3).Should().Be(2.5);
    }

    [Fact]
    public void Average_has_no_integer_division_over_int_long_decimal_and_nullable_columns() => AvgProbeAssertions.SeedAndAssert(_db);

    [Fact]
    public void Average_with_a_repeating_mean_is_within_a_ten_thousandth() => AvgProbeAssertions.SeedRepeatingMeanAndAssert(_db);

    [Fact]
    public void SqlServer_average_casts_integral_operands_to_float_and_leaves_other_ops_alone()
    {
        using var ms = new SqlSugarClient(new ConnectionConfig { DbType = DbType.SqlServer, ConnectionString = "Server=unused;Database=unused", IsAutoCloseConnection = true });
        var sql = AvgProbeAssertions.Query(ms, floatAverageOfIntegers: true).ToSql().Key;
        sql.Should().MatchRegex(@"AVG\(\s*CAST\(\[IntValue\] AS FLOAT\)\s*\)\s+AS\s+\[V0\]");
        sql.Should().MatchRegex(@"AVG\(\s*CAST\(\[LongValue\] AS FLOAT\)\s*\)\s+AS\s+\[V1\]");
        sql.Should().MatchRegex(@"AVG\(\s*CAST\(\[NullableInt\] AS FLOAT\)\s*\)\s+AS\s+\[V3\]");
        sql.Should().MatchRegex(@"AVG\(\s*CAST\(\[BigLong\] AS FLOAT\)\s*\)\s+AS\s+\[V8\]");
        sql.Should().MatchRegex(@"AVG\(\[DecimalValue\]\)\s+AS\s+\[V2\]");
        sql.Should().MatchRegex(@"SUM\(\[IntValue\]\)\s+AS\s+\[V4\]");
    }

    [Fact]
    public void Aggregate_over_an_empty_set_returns_nulls_and_zero_count()
    {
        var proj = (Expression<Func<ArticleTag, AggregateRow>>)ColumnSelectorFactory.AggregateProjection(typeof(ArticleTag),
            [(AggregateOp.Sum, "Sort"), (AggregateOp.Count, "Note")], false);
        var r = _db.Queryable<ArticleTag>().Select(proj).First()!;
        r.V0.Should().BeNull();
        Convert.ToInt64(r.V1).Should().Be(0);
    }

    [Fact]
    public void Aggregate_projection_rejects_more_than_ten_slots()
    {
        var slots = Enumerable.Range(0, 11).Select(_ => (AggregateOp.Count, "Sort")).ToList();
        var act = () => ColumnSelectorFactory.AggregateProjection(typeof(ArticleTag), slots, false);
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
        var deletedAt = Col<Category>("DeletedAt");
        exclude.Should().MatchRegex($@"{deletedAt}\s+IS\s+NULL");
        with.Should().NotMatchRegex($@"{deletedAt}\s+IS\s+(NOT\s+)?NULL");
        only.Should().MatchRegex($@"{deletedAt}\s+IS\s+NOT\s+NULL");
        only.Should().NotMatchRegex($@"{deletedAt}\s+IS\s+NULL\b(?!\s*OR)");
    }
}
