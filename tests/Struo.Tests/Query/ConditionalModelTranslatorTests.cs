// tests/Struo.Tests/Query/ConditionalModelTranslatorTests.cs
using System.Globalization;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Sample.Blog;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class ConditionalModelTranslatorTests
{
    private static (ISqlSugarClient db, EntityDescriptor d, SqliteTestDatabase file) Setup()
    {
        var file = new SqliteTestDatabase();
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<Article>();
        // title moved to the translation sidecar; use own-collection fields (status/publishedAt).
        var fieldToProp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = "Status", ["publishedAt"] = "PublishedAt",
                ["id"] = "Id", ["categoryId"] = "CategoryId"
            };
        var d = new EntityDescriptor(typeof(Article), fieldToProp, "Id");
        return (db, d, file);
    }

    [Fact]
    public void Translates_single_equality()
    {
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("status", QueryOperator.Eq, "published"), null, [], d, db);

            list.Should().ContainSingle();
            var cm = (ConditionalModel)list[0];
            cm.FieldName.Should().Be("Status");
            cm.ConditionalType.Should().Be(ConditionalType.Equal);
            cm.FieldValue.Should().Be("published");
        }
    }

    [Fact]
    public void Guid_equality_sets_csharp_type_name_for_postgres_cast()
    {
        // Postgres-only regression (SQLite is loosely typed and never surfaces this): an
        // untyped string FieldValue against a uuid column produces "operator does not exist:
        // uuid = text" (42883). SqlSugar casts the parameter when CSharpTypeName is set.
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("id", QueryOperator.Eq, Guid.NewGuid().ToString()), null, [], d, db);

            list.Should().ContainSingle();
            ((ConditionalModel)list[0]).CSharpTypeName.Should().Be("guid");
        }
    }

    [Fact]
    public void Nullable_guid_relation_fk_equality_sets_csharp_type_name()
    {
        // CategoryId is Guid? — the underlying-type unwrap must still resolve to "guid".
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("categoryId", QueryOperator.Eq, Guid.NewGuid().ToString()), null, [], d, db);

            ((ConditionalModel)list[0]).CSharpTypeName.Should().Be("guid");
        }
    }

    [Fact]
    public void String_equality_leaves_csharp_type_name_null_no_regression()
    {
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("status", QueryOperator.Eq, "published"), null, [], d, db);

            ((ConditionalModel)list[0]).CSharpTypeName.Should().BeNull();
        }
    }

    [Fact]
    public void Translates_contains_to_like()
    {
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("status", QueryOperator.Contains, "x"), null, [], d, db);
            ((ConditionalModel)list[0]).ConditionalType.Should().Be(ConditionalType.Like);
        }
    }

    [Fact]
    public void Search_builds_or_group_over_searchable_fields()
    {
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(null, "hello", ["status"], d, db);
            list.Should().ContainSingle();
            list[0].Should().BeOfType<ConditionalCollections>();
        }
    }

    [Fact]
    public void Search_group_connects_to_a_preceding_filter_with_and_not_or()
    {
        // Mirrors FilterTranslatorSqlShapeTests.Search_group_connects_to_a_preceding_filter_with_and_not_or:
        // the multi-arg Translate overload builds its own search-group ConditionalCollections and must
        // not repeat the connector defect fix round 3 removed from FilterTranslator (every entry, including
        // the first, wrongly assigned WhereType.Or — rendering "<filter> OR (search…)").
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("status", QueryOperator.Eq, "published"), "hello", ["status"], d, db);
            var sql = db.Queryable<Article>().Where(list).ToSql().Key;

            sql.Should().MatchRegex(@"=\s*@\w+\s+AND\s*\(\s*[\s\S]*?LIKE");
            sql.Should().NotMatchRegex(@"=\s*@\w+\s+OR\s*\(\s*[\s\S]*?LIKE");
        }
    }

    [Fact]
    public void Nested_logical_filter_throws()
    {
        // QueryValidator rejects nested groups before they reach the translator; this
        // test exercises the translator's own internal-invariant guard as defense-in-depth.
        var (db, d, file) = Setup();
        using (file)
        {
            var inner = new LogicalFilter(LogicalOperator.Or,
                [new ComparisonFilter("title", QueryOperator.Contains, "x")]);
            var outer = new LogicalFilter(LogicalOperator.And, [inner]);
            var act = () => ConditionalModelTranslator.Translate(outer, null, [], d, db);
            act.Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void In_operator_serializes_value_list()
    {
        // SqlSugar's ConditionalType.In expects a comma-joined string for FieldValue.
        var (db, d, file) = Setup();
        using (file)
        {
            var filter = new ComparisonFilter("status", QueryOperator.In, new List<object?> { "a", "b" });
            var list = ConditionalModelTranslator.Translate(filter, null, [], d, db);
            list.Should().ContainSingle();
            var cm = (ConditionalModel)list[0];
            cm.ConditionalType.Should().Be(ConditionalType.In);
            cm.FieldValue.Should().Be("a,b");
        }
    }

    [Fact]
    public void Null_operator_generates_is_null_without_empty_string()
    {
        // _null must emit a portable "IS NULL" with no "= ''" comparand. IsNullOrEmpty
        // would emit "OR col = ''", which throws a type error on PostgreSQL for the
        // non-text PublishedAt (timestamptz) column.
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("publishedAt", QueryOperator.Null, null), null, [], d, db);

            // SqlSugar emits irregular internal whitespace (e.g. "  IS NOT  NULL"), so
            // collapse runs of whitespace to single spaces before asserting on phrasing.
            var sql = Normalize(db.Queryable<Article>().Where(list).ToSqlString());

            sql.Should().ContainEquivalentOf("IS NULL");
            sql.Should().NotContain("= ''");
            sql.Should().NotContain("=''");
        }
    }

    [Fact]
    public void NotNull_operator_generates_is_not_null()
    {
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("publishedAt", QueryOperator.NNull, null), null, [], d, db);

            var sql = Normalize(db.Queryable<Article>().Where(list).ToSqlString());

            sql.Should().ContainEquivalentOf("IS NOT NULL");
            sql.Should().NotContain("= ''");
            sql.Should().NotContain("=''");
        }
    }

    // ── culture round-trip (translator render -> SqlSugar re-parse) ────────────────────────
    // A dedicated fixture with REAL decimal/DateTime columns: Article has neither (its own fields
    // are string/Guid/nullable-DateTime-as-null-check only), and SqlSugar only re-parses
    // ConditionalModel.FieldValue through CSharpTypeName-driven type conversion when the column's
    // CLR type is actually numeric/date. Kept separate from Setup()/Article so the rest of this
    // file (and its Article-based fixture) stays untouched.

    [SugarTable("cmt_culture_probe")]
    private sealed class CultureProbeRow
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public decimal Price { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    private static (ISqlSugarClient db, EntityDescriptor d, SqliteTestDatabase file) SetupCultureProbe()
    {
        var file = new SqliteTestDatabase();
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<CultureProbeRow>();
        var fieldToProp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "Id", ["price"] = "Price", ["occurredAt"] = "OccurredAt",
        };
        var d = new EntityDescriptor(typeof(CultureProbeRow), fieldToProp, "Id");
        return (db, d, file);
    }

    [Fact]
    public void Decimal_comparison_round_trips_correctly_under_the_invariant_culture()
    {
        // ConditionalModelTranslator always RENDERS a filter value with CultureInfo.InvariantCulture
        // (ToFieldValue), but SqlSugar re-parses ConditionalModel.FieldValue back to the column's
        // real CLR type using CultureInfo.CurrentCulture, keyed off CSharpTypeName — verified against
        // SqlSugarCore 5.1.4.217: under de-DE (comma decimal separator), a rendered "1.5" reparses as
        // 15, because SqlSugar has no way to be told "parse this literal invariantly" — there is no
        // override. The render and reparse steps therefore only ever agree when the PROCESS's current
        // culture is ALSO invariant, which is exactly what Program.cs now sets at startup
        // (CultureInfo.DefaultThreadCurrentCulture). This test proves the render+reparse PAIR is
        // self-consistent under that culture, by running the real, unparameterized SQL literal
        // (ToSqlString()) SqlSugar emits back through Convert; ProgramCultureTests (API-level) proves
        // Program.cs actually sets it for every real request, not just this direct unit-level pairing.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var (db, d, file) = SetupCultureProbe();
            using (file)
            using (db)
            {
                var list = ConditionalModelTranslator.Translate(
                    new ComparisonFilter("price", QueryOperator.Gt, 1.5m), null, [], d, db);
                var sql = db.Queryable<CultureProbeRow>().Where(list).ToSqlString();

                sql.Should().Contain("1.5", "the invariant-rendered literal must re-parse back to 1.5, not 15 (a de-DE misparse) or 1,5");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void DateTime_comparison_round_trips_correctly_under_the_invariant_culture()
    {
        // Same pairing as the decimal test above, but for a DateTime column: Convert.ToString(DateTime,
        // InvariantCulture) renders month-before-day ("08/09/2026 …", invariant's short-date pattern),
        // which SqlSugar's own re-parse (CultureInfo.CurrentCulture-driven) would silently read as
        // day-before-month under a culture like de-DE — turning August 9 into September 8 with no
        // exception. Proven only under the invariant culture, for the same reason as the decimal test.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var (db, d, file) = SetupCultureProbe();
            using (file)
            using (db)
            {
                var when = new DateTime(2026, 8, 9, 3, 0, 0, DateTimeKind.Unspecified);
                var list = ConditionalModelTranslator.Translate(
                    new ComparisonFilter("occurredAt", QueryOperator.Eq, when), null, [], d, db);
                var sql = db.Queryable<CultureProbeRow>().Where(list).ToSqlString();

                sql.Should().Contain("2026-08-09", "August 9 must not have been misread as day-before-month (September 8)");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>Collapses runs of whitespace to a single space so SQL-phrase assertions
    /// are not defeated by SqlSugar's irregular internal spacing.</summary>
    private static string Normalize(string sql) =>
        System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ");
}
