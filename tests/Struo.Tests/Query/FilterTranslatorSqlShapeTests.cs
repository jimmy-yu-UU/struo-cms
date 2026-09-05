// tests/Struo.Tests/Query/FilterTranslatorSqlShapeTests.cs
using System.Linq.Expressions;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
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

    // ConditionalType.In (not Equal) is deliberate: SqlSugar parameterizes Equal's FieldValue
    // (a real @ConditName0 binding), but inlines In's FieldValue as an escaped SQL literal with an
    // empty parameter list — the same convention WhereInQueries.cs already relies on for IN clauses.
    // A single name is a degenerate one-element IN list, semantically equivalent to Equal here.
    private KeyValuePair<string, List<SugarParameter>> CategoryIdsNamed(string name)
    {
        var sel = (Expression<Func<Category, Guid>>)ColumnSelectorFactory.TypedSelector(typeof(Category), "Id");
        return _db.Queryable<Category>()
            .Where(new List<IConditionalModel> { new ConditionalModel { FieldName = "Name", ConditionalType = ConditionalType.In, FieldValue = name } })
            .Select(sel).ToSql();
    }

    [Fact]
    public void Typed_selector_projects_a_single_column_with_no_parameters()
    {
        var sub = CategoryIdsNamed("Tech");
        sub.Key.Should().Contain("SELECT").And.Contain("FROM");
        sub.Key.Should().NotContain("SqlSugar.");
        sub.Value.Should().BeEmpty("S1: values are inlined as literals; a non-empty list would need parameter renaming");
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
}
