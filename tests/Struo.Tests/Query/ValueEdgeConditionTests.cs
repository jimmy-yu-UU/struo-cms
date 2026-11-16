// tests/Struo.Tests/Query/ValueEdgeConditionTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Query;
using Struo.Infrastructure.Query;
using Struo.Tests.Security.QueryValueInjection;
using Xunit;
using static Struo.Tests.Security.QueryValueInjection.ValueTestKit;

namespace Struo.Tests.Query;

/// <summary>A column stored as JSON text next to the plain scalar tables of <see cref="ValueTestKit"/>.</summary>
[SugarTable("vc_json")]
public sealed class VcJson
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(IsJson = true)] public List<string> Tags { get; set; } = [];
}

/// <summary>
/// The edges of a filter value: the words null and [null] on a text column, string operators on a field
/// that is not text, null values, and lists given to an operator that takes one value.
/// </summary>
public sealed class ValueEdgeConditionTests : IDisposable
{
    private readonly UnitDb _handle = new();
    private ISqlSugarClient Db => _handle.Db;
    private static readonly Struo.Application.Metadata.EntityDescriptor Strings = Descriptor<VcString>("Id", "Code", "Other");
    private static readonly Struo.Application.Metadata.EntityDescriptor Typed = Descriptor<VcTyped>(TypedProps);
    private static readonly Struo.Application.Metadata.EntityDescriptor Json = Descriptor<VcJson>("Id", "Tags");

    public ValueEdgeConditionTests() => SeedStrings(Db);

    public void Dispose() => _handle.Dispose();

    private List<long> RunStrings(string field, QueryOperator op, object? value) =>
        Ids<VcString>(Db, Conds(Db, Strings, field, op, value), r => r.Id);

    // ── _eq with the words null and [null] ───────────────────────────────

    [Fact]
    public void Eq_with_the_word_null_compares_that_text_and_not_the_null_test()
    {
        RunStrings("code", QueryOperator.Eq, "null").Should().Equal(4);
        RunStrings("code", QueryOperator.Eq, "[null]").Should().Equal(5);
        RunStrings("code", QueryOperator.Eq, "").Should().Equal(2);
        RunStrings("code", QueryOperator.Eq, "abc").Should().Equal(1);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    public void Rendering_the_same_condition_list_twice_gives_the_same_statement_and_rows(string word)
    {
        var conds = Conds(Db, Strings, "code", QueryOperator.Eq, word);
        var first = SqlOf<VcString>(Db, conds);
        var second = SqlOf<VcString>(Db, conds);
        second.Sql.Should().Be(first.Sql);
        second.Pars.Select(p => p.Value).Should().Equal(first.Pars.Select(p => p.Value));
        first.Sql.Should().NotMatchRegex(@"(?i)\bcode\W+is null");
        Ids<VcString>(Db, conds, r => r.Id).Should().Equal(Ids<VcString>(Db, conds, r => r.Id));
    }

    [Fact]
    public void Count_and_page_of_one_condition_list_agree_for_the_word_null()
    {
        var conds = Conds(Db, Strings, "code", QueryOperator.Eq, "[null]");
        var count = Db.Queryable<VcString>().Where(conds).Count();
        var page = Db.Queryable<VcString>().Where(conds).ToList();
        page.Should().HaveCount(count).And.ContainSingle().Which.Id.Should().Be(5);
    }

    [Fact]
    public void Neq_and_the_null_checks_are_unchanged()
    {
        RunStrings("code", QueryOperator.Neq, "null").Should().NotContain(4);
        RunStrings("code", QueryOperator.Null, null).Should().Equal(3);
        RunStrings("code", QueryOperator.NNull, null).Should().Equal(1, 2, 4, 5, 6, 7, 8, 9);
    }

    [Fact]
    public void The_word_null_inside_an_or_group_is_compared_as_text()
    {
        var terms = ValueConditions.OrMembers(
            new ValueTarget("Code", "code", typeof(string), Db.CurrentConnectionConfig.DbType), QueryOperator.Eq, "null");
        terms.Models.Should().ContainSingle();
        var list = new List<IConditionalModel>
        {
            new ConditionalCollections
            {
                ConditionalList = [new KeyValuePair<WhereType, ConditionalModel>(WhereType.And, terms.Models[0])],
            },
        };
        Ids<VcString>(Db, list, r => r.Id).Should().Equal(4);
    }

    [Theory]
    [InlineData("null", 4)]
    [InlineData("[null]", 5)]
    public void An_internal_lookup_of_a_text_key_that_is_the_word_null_finds_that_row(string word, long id)
    {
        var target = new ValueTarget("Code", "Code", typeof(string), Db.CurrentConnectionConfig.DbType);
        var eq = ValueConditions.BuildInternal(target, QueryOperator.Eq, word)!;
        var set = ValueConditions.BuildInternal(target, QueryOperator.In, new List<object> { word })!;
        Ids<VcString>(Db, [eq], r => r.Id).Should().Equal(id);
        Ids<VcString>(Db, [set], r => r.Id).Should().Equal(id);
    }

    // ── string operators ─────────────────────────────────────────────────

    private static readonly string[] TextOperatorTokens = ["_contains", "_starts_with", "_ends_with"];

    public static TheoryData<string, QueryOperator, string> NonTextStringOperators()
    {
        var data = new TheoryData<string, QueryOperator, string>();
        foreach (var field in TypedProps.Select(p => char.ToLowerInvariant(p[0]) + p[1..]))
        {
            data.Add(field, QueryOperator.Contains, "_contains");
            data.Add(field, QueryOperator.StartsWith, "_starts_with");
            data.Add(field, QueryOperator.EndsWith, "_ends_with");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(NonTextStringOperators))]
    public void A_string_operator_on_a_field_that_is_not_text_is_a_400_that_names_what_works(
        string field, QueryOperator op, string token)
    {
        var act = () => Conds(Db, Typed, field, op, "marker-12345");
        var message = act.Should().Throw<QueryException>().Which.Message;
        message.Should().Contain($"'{field}'").And.Contain($"'{token}'")
            .And.Contain("_eq").And.Contain("_in").And.Contain("_gte").And.Contain("_null")
            .And.NotContain("marker-12345");
        foreach (var other in TextOperatorTokens.Where(t => t != token))
            message.Should().NotContain($"{other},").And.NotContain($" {other}.");
    }

    [Fact]
    public void A_string_operator_on_a_text_column_and_on_a_JSON_column_still_runs()
    {
        RunStrings("code", QueryOperator.Contains, "bc").Should().Equal(1);
        RunStrings("code", QueryOperator.StartsWith, "x").Should().Equal(7, 8);
        RunStrings("code", QueryOperator.EndsWith, "z").Should().Equal(9);

        Reset<VcJson>(Db);
        Db.Insertable(new VcJson { Id = 1, Tags = ["alpha", "beta"] }).ExecuteCommand();
        Db.Insertable(new VcJson { Id = 2, Tags = ["gamma"] }).ExecuteCommand();
        Ids<VcJson>(Db, Conds(Db, Json, "tags", QueryOperator.Contains, "alpha"), r => r.Id).Should().Equal(1);
        Ids<VcJson>(Db, Conds(Db, Json, "tags", QueryOperator.Contains, "gam"), r => r.Id).Should().Equal(2);
    }

    [Fact]
    public void Comparison_set_and_null_operators_still_run_on_every_non_text_field()
    {
        SeedTyped(Db);
        Ids<VcTyped>(Db, Conds(Db, Typed, "dateTimeCol", QueryOperator.Gte, "2026-03-05"), r => r.Id)
            .Should().NotBeEmpty();
        Ids<VcTyped>(Db, Conds(Db, Typed, "intCol", QueryOperator.In, "10,30"), r => r.Id).Should().Equal(1, 3);
        Ids<VcTyped>(Db, Conds(Db, Typed, "guidCol", QueryOperator.NNull, null), r => r.Id).Should().Equal(1, 2, 3);
    }

    // ── null values ──────────────────────────────────────────────────────

    public static TheoryData<string, QueryOperator, string> NullValues()
    {
        var data = new TheoryData<string, QueryOperator, string>();
        var ops = new (QueryOperator Op, string Token)[]
        {
            (QueryOperator.Eq, "_eq"), (QueryOperator.Neq, "_neq"), (QueryOperator.Gt, "_gt"), (QueryOperator.Gte, "_gte"),
            (QueryOperator.Lt, "_lt"), (QueryOperator.Lte, "_lte"), (QueryOperator.Contains, "_contains"),
            (QueryOperator.StartsWith, "_starts_with"), (QueryOperator.EndsWith, "_ends_with"),
            (QueryOperator.In, "_in"), (QueryOperator.Nin, "_nin"),
        };
        foreach (var field in new[] { "code", "intCol", "guidCol", "dateTimeCol", "boolCol", "enumCol" })
            foreach (var (op, token) in ops)
                data.Add(field, op, token);
        return data;
    }

    [Theory]
    [MemberData(nameof(NullValues))]
    public void A_null_value_is_a_400_that_points_to_null_and_nnull(string field, QueryOperator op, string token)
    {
        var d = field == "code" ? Strings : Typed;
        var act = () => Conds(Db, d, field, op, null);
        var message = act.Should().Throw<QueryException>().Which.Message;
        message.Should().Contain($"'{field}'").And.Contain($"'{token}'").And.Contain("_null").And.Contain("_nnull");
    }

    [Fact]
    public void The_null_operators_ignore_the_value_as_before()
    {
        RunStrings("code", QueryOperator.Null, null).Should().Equal(3);
        RunStrings("code", QueryOperator.Null, "ignored").Should().Equal(3);
        RunStrings("code", QueryOperator.NNull, null).Should().HaveCount(8);
    }

    // ── lists for single-value operators ─────────────────────────────────

    [Theory]
    [InlineData("code", QueryOperator.Eq, "_eq")]
    [InlineData("code", QueryOperator.Neq, "_neq")]
    [InlineData("code", QueryOperator.Gt, "_gt")]
    [InlineData("code", QueryOperator.Contains, "_contains")]
    [InlineData("code", QueryOperator.StartsWith, "_starts_with")]
    [InlineData("code", QueryOperator.EndsWith, "_ends_with")]
    [InlineData("intCol", QueryOperator.Eq, "_eq")]
    [InlineData("guidCol", QueryOperator.Neq, "_neq")]
    public void A_list_for_a_single_value_operator_is_a_400(string field, QueryOperator op, string token)
    {
        var d = field == "code" ? Strings : Typed;
        var act = () => Conds(Db, d, field, op, new List<object?> { "a", "b" });
        act.Should().Throw<QueryException>().Which.Message
            .Should().Contain($"'{field}'").And.Contain($"'{token}'").And.Contain("single value");
    }

    [Fact]
    public void A_list_is_still_the_value_of_a_set_operator()
    {
        RunStrings("code", QueryOperator.In, new List<object?> { "abc", "zz" }).Should().Equal(1, 9);
        RunStrings("code", QueryOperator.Nin, new List<object?> { "abc" }).Should().Contain(2);
    }
}
