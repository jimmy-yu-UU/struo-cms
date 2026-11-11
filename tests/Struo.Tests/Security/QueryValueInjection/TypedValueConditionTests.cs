// tests/Struo.Tests/Security/QueryValueInjection/TypedValueConditionTests.cs
using System.Globalization;
using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Query;
using Struo.Infrastructure.Query;
using Xunit;
using static Struo.Tests.Security.QueryValueInjection.ValueTestKit;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>Every supported column type parses each filter value from its own CLR type and renders the parsed value.</summary>
public sealed class TypedValueConditionTests : IDisposable
{
    private readonly UnitDb _handle = new();
    private ISqlSugarClient Db => _handle.Db;
    private static readonly Struo.Application.Metadata.EntityDescriptor D = Descriptor<VcTyped>(TypedProps);

    public TypedValueConditionTests() => SeedTyped(Db);

    public void Dispose() => _handle.Dispose();

    private List<long> Run(string field, QueryOperator op, object? value) =>
        Ids<VcTyped>(Db, Conds(Db, D, field, op, value), r => r.Id);

    // field, row-1 value, row-3 value, row-1 value as text, row-3 value as text, ordered
    public static TheoryData<string, object, object, string, string, bool> Scalars() => new()
    {
        { "id", 1L, 3L, "1", "3", true },
        { "intCol", 10L, 30L, "10", "30", true },
        { "longCol", 10_000_000_000L, 30_000_000_000L, "10000000000", "30000000000", true },
        { "shortCol", 100L, 300L, "100", "300", true },
        { "byteCol", 10L, 30L, "10", "30", true },
        { "decimalCol", 1.5d, 4.5d, "1.5", "4.5", true },
        { "doubleCol", 2.5d, 7.5d, "2.5", "7.5", true },
        { "floatCol", 0.5d, 1.5d, "0.5", "1.5", true },
        { "guidCol", Guids[0].ToString(), Guids[2].ToString("N"), Guids[0].ToString("D"), Guids[2].ToString("B"), false },
    };

    [Theory]
    [MemberData(nameof(Scalars))]
    public void Set_operators_select_the_rows_for_a_list_and_for_a_comma_separated_string(
        string field, object v1, object v3, string t1, string t3, bool ordered)
    {
        _ = ordered;
        Run(field, QueryOperator.In, new List<object?> { v1, v3 }).Should().Equal(1, 3);
        Run(field, QueryOperator.In, $"{t1},{t3}").Should().Equal(1, 3);
        Run(field, QueryOperator.In, new List<object?> { v1 }).Should().Equal(1);
        Run(field, QueryOperator.Nin, new List<object?> { v1 }).Should().Equal(2, 3);
        Run(field, QueryOperator.Nin, $"{t1},{t3}").Should().Equal(2);
    }

    [Theory]
    [MemberData(nameof(Scalars))]
    public void Comparison_operators_select_the_rows(
        string field, object v1, object v3, string t1, string t3, bool ordered)
    {
        _ = v3;
        _ = t3;
        Run(field, QueryOperator.Eq, v1).Should().Equal(1);
        Run(field, QueryOperator.Eq, t1).Should().Equal(1);
        Run(field, QueryOperator.Neq, t1).Should().Equal(2, 3);
        if (!ordered) return;
        Run(field, QueryOperator.Gt, t1).Should().Equal(2, 3);
        Run(field, QueryOperator.Gte, t1).Should().Equal(1, 2, 3);
        Run(field, QueryOperator.Lt, t1).Should().BeEmpty();
        Run(field, QueryOperator.Lte, v1).Should().Equal(1);
    }

    // Row equality on a date-time depends on the fraction the backend stores, so the comparisons sit half a day
    // away from the stored values and the equality forms are checked on the rendered value.
    [Fact]
    public void Date_time_values_compare_as_utc_instants()
    {
        Run("dateTimeCol", QueryOperator.Gt, "2026-03-05T12:00:00Z").Should().Equal(2, 3);
        Run("dateTimeCol", QueryOperator.Lt, "2026-03-06T00:00:00+00:00").Should().Equal(1);
        Run("dateTimeCol", QueryOperator.Gte, "2026-03-05T20:00:00+08:00").Should().Equal(2, 3);
        Run("dateTimeCol", QueryOperator.Lte, new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero)).Should().Equal(1);

        var inModel = Conds(Db, D, "dateTimeCol", QueryOperator.In, "2026-03-05T05:06:07Z,2026-03-07T13:06:07+08:00")
            .Should().ContainSingle().Which.Should().BeOfType<ConditionalModel>().Which;
        inModel.FieldValue.Should().Contain("2026-03-05 05:06:07").And.Contain("2026-03-07 05:06:07");
        inModel.ConditionalType.Should().Be(ConditionalType.In);
    }

    [Fact]
    public void Booleans_accept_true_false_and_one_zero_in_every_form()
    {
        Run("boolCol", QueryOperator.In, new List<object?> { true }).Should().Equal(1, 3);
        Run("boolCol", QueryOperator.In, "true").Should().Equal(1, 3);
        Run("boolCol", QueryOperator.In, "False").Should().Equal(2);
        Run("boolCol", QueryOperator.In, new List<object?> { 1L }).Should().Equal(1, 3);
        Run("boolCol", QueryOperator.In, "0").Should().Equal(2);
        Run("boolCol", QueryOperator.In, "true,false").Should().Equal(1, 2, 3);
        Run("boolCol", QueryOperator.Nin, new List<object?> { false }).Should().Equal(1, 3);
        Run("boolCol", QueryOperator.Nin, "true").Should().Equal(2);
        Run("boolCol", QueryOperator.Eq, false).Should().Equal(2);
        Run("boolCol", QueryOperator.Neq, "true").Should().Equal(2);
    }

    [Fact]
    public void Enums_accept_a_member_name_or_its_number()
    {
        // rows 1..3 hold Blue, Green, Red
        Run("enumCol", QueryOperator.In, new List<object?> { "Blue", "red" }).Should().Equal(1, 3);
        Run("enumCol", QueryOperator.In, "1,0").Should().Equal(1, 3);
        Run("enumCol", QueryOperator.In, new List<object?> { 2L }).Should().Equal(2);
        Run("enumCol", QueryOperator.Nin, "Green").Should().Equal(1, 3);
    }

    [Fact]
    public void Decimal_and_double_text_is_read_with_the_invariant_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var asText = Conds(Db, D, "decimalCol", QueryOperator.In, "1.5,4.5");
            asText.Should().ContainSingle().Which.Should().BeOfType<ConditionalModel>().Which.FieldValue.Should().Be("1.5,4.5");
            var asNumber = Conds(Db, D, "doubleCol", QueryOperator.In, new List<object?> { 2.5d, 7.5d });
            asNumber.Should().ContainSingle().Which.Should().BeOfType<ConditionalModel>().Which.FieldValue.Should().Be("2.5,7.5");
            Run("decimalCol", QueryOperator.In, "1.5,4.5").Should().Equal(1, 3);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    public static TheoryData<string> TypedFields() =>
        new("id", "intCol", "longCol", "shortCol", "byteCol", "boolCol", "decimalCol", "doubleCol", "floatCol",
            "guidCol", "dateTimeCol", "enumCol");

    [Theory]
    [MemberData(nameof(TypedFields))]
    public void A_value_that_is_not_of_the_column_type_is_a_400_for_every_operator(string field)
    {
        const string bad = "0 OR 1=1";
        foreach (var op in new[]
                 {
                     QueryOperator.Eq, QueryOperator.Neq, QueryOperator.Gt, QueryOperator.Gte, QueryOperator.Lt,
                     QueryOperator.Lte, QueryOperator.In, QueryOperator.Nin,
                 })
        {
            var act = () => Conds(Db, D, field, op, bad);
            act.Should().Throw<QueryException>($"{field} {op}").Which.Message.Should().Contain(field);
        }
    }

    [Theory]
    [MemberData(nameof(TypedFields))]
    public void The_error_names_the_field_and_does_not_echo_the_value_list(string field)
    {
        var list = new List<object?> { "marker-first-77", "marker-second-88" };
        var act = () => Conds(Db, D, field, QueryOperator.In, list);
        var ex = act.Should().Throw<QueryException>().Which;
        ex.Message.Should().Contain(field).And.NotContain("marker-");
    }

    [Theory]
    [InlineData("intCol", "99999999999")]
    [InlineData("shortCol", "40000")]
    [InlineData("byteCol", "256")]
    [InlineData("byteCol", "-1")]
    [InlineData("intCol", "1.5")]
    [InlineData("doubleCol", "NaN")]
    [InlineData("doubleCol", "Infinity")]
    [InlineData("floatCol", "1e999")]
    [InlineData("enumCol", "7")]
    [InlineData("boolCol", "yes")]
    [InlineData("boolCol", "2")]
    [InlineData("guidCol", "not-a-guid")]
    [InlineData("dateTimeCol", "not-a-date")]
    public void Out_of_range_and_malformed_values_are_refused(string field, string value)
    {
        var act = () => Conds(Db, D, field, QueryOperator.In, value);
        act.Should().Throw<QueryException>();
        var eq = () => Conds(Db, D, field, QueryOperator.Eq, value);
        eq.Should().Throw<QueryException>();
    }

    [Fact]
    public void One_enum_value_is_one_member_not_a_combination()
    {
        var act = () => Conds(Db, D, "enumCol", QueryOperator.Eq, "Red,Blue");
        act.Should().Throw<QueryException>();
    }

    [Fact]
    public void A_null_element_in_a_set_is_a_400_and_a_nested_list_is_too()
    {
        var withNull = () => Conds(Db, D, "intCol", QueryOperator.In, new List<object?> { 10L, null });
        withNull.Should().Throw<QueryException>();
        var nested = () => Conds(Db, D, "intCol", QueryOperator.Nin, new List<object?> { new List<object?> { 10L } });
        nested.Should().Throw<QueryException>();
    }

    [Fact]
    public void An_empty_set_matches_nothing_for_in_and_adds_no_condition_for_nin()
    {
        foreach (var field in new[] { "id", "boolCol", "guidCol", "dateTimeCol", "doubleCol" })
        {
            Run(field, QueryOperator.In, new List<object?>()).Should().BeEmpty(field);
            Run(field, QueryOperator.In, "").Should().BeEmpty(field);
            Conds(Db, D, field, QueryOperator.Nin, new List<object?>()).Should().BeEmpty(field);
            Run(field, QueryOperator.Nin, "").Should().Equal(1, 2, 3);
        }
    }

    [Fact]
    public void An_empty_set_never_yields_an_empty_group()
    {
        var cond = ConditionalModelTranslator.ToCondition(
            new ComparisonFilter("intCol", QueryOperator.In, new List<object?>()), D, Db);
        cond.Should().BeOfType<ConditionalCollections>().Which.ConditionalList.Should().NotBeEmpty();
    }

    [Fact]
    public void Repeated_values_in_different_spellings_match_the_same_row()
    {
        Run("intCol", QueryOperator.In, new List<object?> { 10L, "10", " 10 " }).Should().Equal(1);
        Run("intCol", QueryOperator.Nin, new List<object?> { 10L, "10" }).Should().Equal(2, 3);
        var model = Conds(Db, D, "intCol", QueryOperator.In, new List<object?> { " 0010 " })
            .Should().ContainSingle().Which.Should().BeOfType<ConditionalModel>().Which;
        model.FieldValue.Should().Be("10");
    }

    [Fact]
    public void Rendered_sql_holds_the_parsed_value_and_never_the_original_text()
    {
        var conds = Conds(Db, D, "guidCol", QueryOperator.In, new List<object?> { Guids[0].ToString("N").ToUpperInvariant() });
        var (sql, _) = SqlOf<VcTyped>(Db, conds);
        sql.Should().NotContain(Guids[0].ToString("N").ToUpperInvariant());
        sql.Should().Contain(Guids[0].ToString("D"));

        var spaced = Conds(Db, D, "intCol", QueryOperator.In, new List<object?> { " 0010 " });
        SqlOf<VcTyped>(Db, spaced).Sql.Should().NotContain(" 0010 ").And.Contain("10");
    }

    [Theory]
    [InlineData("boolCol", " TRUE ")]
    [InlineData("decimalCol", "+0001.50")]
    [InlineData("doubleCol", "2.5E0")]
    [InlineData("longCol", " 0010000000000 ")]
    [InlineData("enumCol", " blue ")]
    public void A_set_never_holds_the_original_spelling_of_a_value(string field, string spelling)
    {
        var conds = Conds(Db, D, field, QueryOperator.In, new List<object?> { spelling, spelling.Trim() });
        var (sql, _) = SqlOf<VcTyped>(Db, conds);
        sql.Should().NotContain(spelling.Trim());
    }

    [Fact]
    public void The_soft_delete_filter_still_applies_to_every_shape()
    {
        Run("id", QueryOperator.In, new List<object?> { 1L, 4L }).Should().Equal(1);
        Run("id", QueryOperator.Nin, new List<object?> { 1L }).Should().Equal(2, 3);
    }

    [Fact]
    public void Text_operators_and_null_checks_are_unchanged()
    {
        Run("id", QueryOperator.Null, null).Should().BeEmpty();
        Run("id", QueryOperator.NNull, null).Should().Equal(1, 2, 3);
        var like = Conds(Db, D, "dateTimeCol", QueryOperator.StartsWith, "2026-03-05");
        like.Should().ContainSingle().Which.Should().BeOfType<ConditionalModel>()
            .Which.ConditionalType.Should().Be(ConditionalType.LikeLeft);
    }
}
