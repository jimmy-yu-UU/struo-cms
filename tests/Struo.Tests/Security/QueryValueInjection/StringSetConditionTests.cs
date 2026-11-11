// tests/Struo.Tests/Security/QueryValueInjection/StringSetConditionTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Query;
using Struo.Infrastructure.Query;
using Xunit;
using static Struo.Tests.Security.QueryValueInjection.ValueTestKit;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>String `_in`/`_nin` render as parameterised equality groups; the value never reaches the SQL text.</summary>
public sealed class StringSetConditionTests : IDisposable
{
    private readonly UnitDb _handle = new();
    private ISqlSugarClient Db => _handle.Db;
    private static readonly Struo.Application.Metadata.EntityDescriptor D = Descriptor<VcString>("Id", "Code", "Other");

    public StringSetConditionTests() => SeedStrings(Db);

    public void Dispose() => _handle.Dispose();

    private List<long> Run(QueryOperator op, object? value, string field = "code") =>
        Ids<VcString>(Db, Conds(Db, D, field, op, value), r => r.Id);

    private static List<object?> L(params object?[] values) => [.. values];

    // The two payloads are the smallest strings that would change the statement if they were inlined.
    private const string QuoteBreak = "x' OR 1=1 -- -";
    private const string SlashBreak = "x\\' OR 1=1 -- -";

    [Fact]
    public void In_selects_the_matching_rows_for_awkward_values()
    {
        Run(QueryOperator.In, L("abc", "zz")).Should().Equal(1, 9);
        Run(QueryOperator.In, L("")).Should().Equal(2);
        Run(QueryOperator.In, L("null")).Should().Equal(4);
        Run(QueryOperator.In, L("[null]")).Should().Equal(5);
        Run(QueryOperator.In, L("a,b")).Should().Equal(6);
        Run(QueryOperator.In, L("x\\")).Should().Equal(7);
        Run(QueryOperator.In, L("x'")).Should().Equal(8);
        Run(QueryOperator.In, L("abc", "abc")).Should().Equal(1);
    }

    [Fact]
    public void Nin_excludes_the_matching_rows_and_keeps_null_out_like_a_comparison()
    {
        Run(QueryOperator.Nin, L("abc")).Should().Equal(2, 4, 5, 6, 7, 8, 9);
        Run(QueryOperator.Nin, L("null")).Should().Equal(1, 2, 5, 6, 7, 8, 9);
        Run(QueryOperator.Nin, L("[null]", "")).Should().Equal(1, 4, 6, 7, 8, 9);
        Run(QueryOperator.Nin, L("a,b")).Should().Equal(1, 2, 4, 5, 7, 8, 9);
    }

    [Fact]
    public void A_query_string_value_is_one_comma_separated_list()
    {
        Run(QueryOperator.In, "abc,zz").Should().Equal(1, 9);
        Run(QueryOperator.In, "a,b").Should().BeEmpty();
        Run(QueryOperator.Nin, "abc,zz,null,[null],").Should().Equal(6, 7, 8);
        Run(QueryOperator.In, "abc,,zz").Should().Equal(1, 2, 9);
    }

    [Fact]
    public void An_empty_list_matches_nothing_and_an_empty_nin_adds_no_condition()
    {
        Run(QueryOperator.In, L()).Should().BeEmpty();
        Run(QueryOperator.In, "").Should().BeEmpty();
        Conds(Db, D, "code", QueryOperator.Nin, L()).Should().BeEmpty();
        Run(QueryOperator.Nin, "").Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Fact]
    public void A_null_element_is_a_400()
    {
        var inNull = () => Conds(Db, D, "code", QueryOperator.In, L("abc", null));
        inNull.Should().Throw<QueryException>().Which.Message.Should().Contain("code");
        var ninNull = () => Conds(Db, D, "code", QueryOperator.Nin, L((object?)null));
        ninNull.Should().Throw<QueryException>();
        var scalar = () => Conds(Db, D, "code", QueryOperator.In, null);
        scalar.Should().Throw<QueryException>();
    }

    [Fact]
    public void Payload_strings_are_bound_as_parameters_and_match_nothing()
    {
        foreach (var payload in new[] { QuoteBreak, SlashBreak })
        {
            foreach (var op in new[] { QueryOperator.In, QueryOperator.Nin })
            {
                var conds = Conds(Db, D, "code", op, L("abc", payload));
                var (sql, pars) = SqlOf<VcString>(Db, conds);
                sql.Should().NotContain("OR 1=1");
                pars.Select(p => p.Value?.ToString()).Should().Contain(payload);
                var ids = Ids<VcString>(Db, conds, r => r.Id);
                if (op == QueryOperator.In) ids.Should().Equal(1);
                else ids.Should().Equal(2, 4, 5, 6, 7, 8, 9);
            }
            Run(QueryOperator.In, payload).Should().BeEmpty();
            Run(QueryOperator.In, L(payload)).Should().BeEmpty();
        }
    }

    [Fact]
    public void The_group_has_the_required_shape()
    {
        var inGroup = ConditionalModelTranslator.ToCondition(
            new ComparisonFilter("code", QueryOperator.In, L("abc", "zz", "abc", "x'")), D, Db)
            .Should().BeOfType<ConditionalCollections>().Which;
        var inList = inGroup.ConditionalList;
        inList.Should().HaveCount(3);
        inList[0].Key.Should().Be(WhereType.And);
        inList.Skip(1).Should().OnlyContain(kv => kv.Key == WhereType.Or);
        inList.Should().OnlyContain(kv => kv.Value.ConditionalType == ConditionalType.Equal);
        inList.Select(kv => kv.Value).Distinct(ReferenceEqualityComparer.Instance).Should().HaveCount(3);

        var ninGroup = ConditionalModelTranslator.ToCondition(
            new ComparisonFilter("code", QueryOperator.Nin, L("abc", "zz")), D, Db)
            .Should().BeOfType<ConditionalCollections>().Which;
        ninGroup.ConditionalList[0].Key.Should().Be(WhereType.And);
        ninGroup.ConditionalList.Should().OnlyContain(kv => kv.Key == WhereType.And && kv.Value.ConditionalType == ConditionalType.NoEqual);
    }

    [Fact]
    public void Rendering_the_same_conditions_twice_gives_the_same_statement_and_rows()
    {
        foreach (var value in new[] { "null", "[null]" })
        {
            var conds = Conds(Db, D, "code", QueryOperator.In, L(value));
            var first = SqlOf<VcString>(Db, conds).Sql;
            var firstRows = Ids<VcString>(Db, conds, r => r.Id);
            var second = SqlOf<VcString>(Db, conds).Sql;
            var secondRows = Ids<VcString>(Db, conds, r => r.Id);
            second.Should().Be(first);
            secondRows.Should().Equal(firstRows);
        }
    }

    [Fact]
    public void Soft_deleted_rows_stay_out_whatever_the_group_is_combined_with()
    {
        var group = Conds(Db, D, "code", QueryOperator.In, L("abc"))[0];
        var other = Conds(Db, D, "other", QueryOperator.Eq, "o1")[0];
        Ids<VcString>(Db, [other, group], r => r.Id).Should().Equal(1);
        Ids<VcString>(Db, [group, other], r => r.Id).Should().Equal(1);
        var nin = Conds(Db, D, "code", QueryOperator.Nin, L("zz"))[0];
        Ids<VcString>(Db, [other, nin], r => r.Id).Should().Equal(1, 8);
        Ids<VcString>(Db, [nin, other], r => r.Id).Should().Equal(1, 8);
        Ids<VcString>(Db, [group, nin], r => r.Id).Should().Equal(1);
    }

    [Fact]
    public void Equality_comparisons_on_strings_still_bind_the_value()
    {
        Run(QueryOperator.Eq, "x'").Should().Equal(8);
        Run(QueryOperator.Neq, "abc").Should().Equal(2, 4, 5, 6, 7, 8, 9);
        Run(QueryOperator.Contains, "a").Should().Contain(new long[] { 1, 6 });
    }
}
