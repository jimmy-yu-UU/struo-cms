// tests/Struo.Tests/Query/LikeLiteralConditionTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Query;
using Struo.Tests.Security.QueryValueInjection;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Security.QueryValueInjection.ValueTestKit;

namespace Struo.Tests.Query;

/// <summary>A text column and a JSON column holding the same words.</summary>
[SugarTable("ll_rows")]
public sealed class LlRow
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(Length = 400)] public string Body { get; set; } = "";
    [SugarColumn(IsJson = true)] public List<string> Data { get; set; } = [];
}

/// <summary>The table of <see cref="LlRow"/> with the JSON column read back as the text the database holds.</summary>
[SugarTable("ll_rows")]
public sealed class LlStored
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    public string Body { get; set; } = "";
    public string Data { get; set; } = "";
}

/// <summary>Columns stored as JSON whose CLR type is not a string.</summary>
[SugarTable("ll_complex")]
public sealed class LlComplex
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(IsJson = true)] public LlNote Note { get; set; } = new();
    [SugarColumn(IsJson = true)] public int[] Numbers { get; set; } = [];
    [SugarColumn(IsJson = true)] public Dictionary<string, int> Scores { get; set; } = new();
}

public sealed class LlNote
{
    public string Text { get; set; } = "";
}

/// <summary>
/// The three string operators match their value as plain text on a text column and on a JSON column:
/// a percent sign, an underscore, a bracket and a backslash are ordinary characters.
/// </summary>
public sealed class LikeLiteralConditionTests : IDisposable
{
    private const char Backslash = '\u005c';
    private static readonly string One = new(Backslash, 1);
    private static readonly string Two = new(Backslash, 2);
    private static readonly string Cjk = "\u4e2d";
    private static readonly string CjkPair = "\u4e2d\u6587";

    private static readonly string[] Words =
    [
        "abc", "a_c", "a%c", "axc", "100%", "100x", "1000", "[ab]", "a", "b", "[", "d" + One, One, One + "%",
        Two, "x" + One + "y", "^", "'", "''", "", Cjk, CjkPair, "ab", "ba", new string('a', 200), new string('a', 201),
    ];

    public static readonly string[] Values =
    [
        "%", "_", "[ab]", "[", One, "d" + One, One + "%", "^", "'", "''", "", Cjk, new string('a', 200), "a_c", "100%", Two,
    ];

    public static TheoryData<QueryOperator, int> Cases()
    {
        var data = new TheoryData<QueryOperator, int>();
        foreach (var op in new[] { QueryOperator.Contains, QueryOperator.StartsWith, QueryOperator.EndsWith })
            for (var index = 0; index < Values.Length; index++) data.Add(op, index);
        return data;
    }

    private readonly UnitDb _handle = new();
    private ISqlSugarClient Db => _handle.Db;
    private static readonly Struo.Application.Metadata.EntityDescriptor Rows = Descriptor<LlRow>("Id", "Body", "Data");

    public void Dispose() => _handle.Dispose();

    private void Seed(IReadOnlyList<string> words)
    {
        Reset<LlRow>(Db);
        Db.Insertable(words.Select((w, i) => new LlRow { Id = i + 1, Body = w, Data = [w] }).ToList()).ExecuteCommand();
    }

    private List<long> Run(string field, QueryOperator op, string value) =>
        Ids<LlRow>(Db, Conds(Db, Rows, field, op, value), r => r.Id);

    private static bool Matches(QueryOperator op, string text, string value) => op switch
    {
        QueryOperator.Contains => text.Contains(value, StringComparison.Ordinal),
        QueryOperator.StartsWith => text.StartsWith(value, StringComparison.Ordinal),
        _ => text.EndsWith(value, StringComparison.Ordinal),
    };

    private List<long> Expected(string field, QueryOperator op, string value) =>
        Db.Queryable<LlStored>().ToList()
            .Where(r => Matches(op, field == "body" ? r.Body : r.Data, value))
            .Select(r => r.Id).Order().ToList();

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_value_is_matched_as_plain_text_on_a_text_and_a_JSON_column(QueryOperator op, int index)
    {
        Seed(Words);
        foreach (var field in new[] { "body", "data" })
            Run(field, op, Values[index]).Should().Equal(Expected(field, op, Values[index]), $"{field} {op} value #{index}");
    }

    [Fact]
    public void An_underscore_or_a_percent_sign_selects_only_the_rows_that_hold_it()
    {
        Seed(Words);
        Run("body", QueryOperator.Contains, "a_c").Should().Equal(2);
        Run("body", QueryOperator.StartsWith, "100%").Should().Equal(5);
        Run("body", QueryOperator.EndsWith, "%c").Should().Equal(3);
        Run("data", QueryOperator.Contains, "a%c").Should().Equal(3);
        Run("body", QueryOperator.Contains, "[ab]").Should().Equal(8);
    }

    [Fact]
    public void A_value_that_ends_in_one_backslash_is_a_plain_search_and_not_an_error()
    {
        Seed(Words);
        Run("body", QueryOperator.EndsWith, "d" + One).Should().Equal(12);
        Run("data", QueryOperator.Contains, "d" + One).Should().Equal(12);
    }

    [Fact]
    public void A_JSON_column_of_a_class_an_array_or_a_dictionary_is_matched_as_plain_text()
    {
        Reset<LlComplex>(Db);
        Db.Insertable(new List<LlComplex>
        {
            new() { Id = 1, Note = new LlNote { Text = "x_y" }, Numbers = [1, 2], Scores = new() { ["k_1"] = 5 } },
            new() { Id = 2, Note = new LlNote { Text = "xzy" }, Numbers = [3], Scores = new() { ["kz1"] = 5 } },
        }).ExecuteCommand();
        var descriptor = Descriptor<LlComplex>("Id", "Note", "Numbers", "Scores");
        List<long> Complex(string field, QueryOperator op, string value) =>
            Ids<LlComplex>(Db, Conds(Db, descriptor, field, op, value), r => r.Id);

        Complex("note", QueryOperator.Contains, "x_y").Should().Equal(1);
        Complex("note", QueryOperator.StartsWith, "{\"Text\":\"x_").Should().Equal(1);
        Complex("scores", QueryOperator.Contains, "k_1").Should().Equal(1);
        Complex("scores", QueryOperator.EndsWith, "_1\":5}").Should().Equal(1);
        Complex("numbers", QueryOperator.Contains, "_").Should().BeEmpty();
        Complex("numbers", QueryOperator.StartsWith, "[1,2").Should().Equal(1);
    }

    [Fact]
    public void An_empty_value_matches_every_row()
    {
        Seed(Words);
        var all = Enumerable.Range(1, Words.Length).Select(i => (long)i).ToList();
        foreach (var op in new[] { QueryOperator.Contains, QueryOperator.StartsWith, QueryOperator.EndsWith })
        foreach (var field in new[] { "body", "data" })
            Run(field, op, "").Should().Equal(all, $"{field} {op}");
    }

    // case behaviour is the database's own, with and without a wildcard character in the value

    private static readonly string[] CaseWords = ["Case", "case", "Case%", "case%"];

    [Fact]
    public void Case_behaviour_is_the_same_with_and_without_a_percent_sign_in_the_value()
    {
        Seed(CaseWords);
        var sensitive = TestBackend.Current == TestBackendKind.PostgreSQL;
        Run("body", QueryOperator.Contains, "case").Should().Equal(sensitive ? [2L, 4L] : [1L, 2L, 3L, 4L]);
        Run("body", QueryOperator.Contains, "case%").Should().Equal(sensitive ? [4L] : [3L, 4L]);
        Run("body", QueryOperator.StartsWith, "case").Should().Equal(sensitive ? [2L, 4L] : [1L, 2L, 3L, 4L]);
        Run("body", QueryOperator.StartsWith, "case%").Should().Equal(sensitive ? [4L] : [3L, 4L]);
        Run("body", QueryOperator.EndsWith, "case").Should().Equal(sensitive ? [2L] : [1L, 2L]);
        Run("body", QueryOperator.EndsWith, "case%").Should().Equal(sensitive ? [4L] : [3L, 4L]);
    }

    // the statement

    [Theory]
    [InlineData("abc", false)]
    [InlineData("a_c", true)]
    [InlineData("100%", true)]
    [InlineData("[ab]", false)]
    [InlineData("d\u005c", false)]
    public void A_subquery_appears_only_on_SQLite_and_only_for_a_value_with_a_percent_sign_or_underscore(string value, bool wildcard)
    {
        Seed(Words);
        var sql = SqlOf<LlRow>(Db, Conds(Db, Rows, "body", QueryOperator.Contains, value));
        var expectSubquery = TestBackend.Current == TestBackendKind.Sqlite && wildcard;
        sql.Sql.Contains("(SELECT", StringComparison.OrdinalIgnoreCase).Should().Be(expectSubquery, sql.Sql);
    }

    [Fact]
    public void Each_backend_binds_the_value_in_its_own_escaped_form()
    {
        Seed(Words);
        var sql = SqlOf<LlRow>(Db, Conds(Db, Rows, "body", QueryOperator.Contains, "a%_[" + One));
        var bound = sql.Pars.Select(p => p.Value as string).Single(v => v is not null && v.Contains('a'));
        var expected = TestBackend.Current switch
        {
            TestBackendKind.SqlServer => "%a[%][_][[]" + One + "%",
            TestBackendKind.Sqlite => "a" + One + "%" + One + "_[" + Two,
            _ => "%a" + One + "%" + One + "_[" + Two + "%",
        };
        bound.Should().Be(expected);
    }

    [Fact]
    public void Count_and_page_of_one_condition_list_agree()
    {
        Seed(Words);
        var conds = Conds(Db, Rows, "body", QueryOperator.Contains, "a_c");
        Db.Queryable<LlRow>().Where(conds).Count().Should().Be(Db.Queryable<LlRow>().Where(conds).ToList().Count).And.Be(1);
    }

    [Fact]
    public void Two_conditions_with_a_percent_sign_in_one_statement_bind_their_own_values()
    {
        Seed(Words);
        var conds = Conds(Db, Rows, "body", QueryOperator.Contains, "a_c");
        conds.AddRange(Conds(Db, Rows, "body", QueryOperator.EndsWith, "c"));
        conds.AddRange(Conds(Db, Rows, "data", QueryOperator.StartsWith, "[" + "\"a%"));
        var sql = SqlOf<LlRow>(Db, conds);
        sql.Pars.Select(p => p.ParameterName).Should().OnlyHaveUniqueItems();
        Db.Queryable<LlRow>().Where(conds).ToList().Should().BeEmpty("no row is both a_c and starts with a%");
        var two = Conds(Db, Rows, "body", QueryOperator.Contains, "a_c");
        two.AddRange(Conds(Db, Rows, "data", QueryOperator.Contains, "a_c"));
        Db.Queryable<LlRow>().Where(two).ToList().Select(r => r.Id).Should().Equal(2);
    }
}
