// tests/Struo.Tests/Security/QueryValueInjection/ParameterNameCollisionTests.cs
using AwesomeAssertions;
using Struo.Domain.Query;
using Struo.Infrastructure.Query;
using Xunit;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>
/// SqlSugar names a condition's parameter after its column plus a position number, with nothing between
/// them, so a column X and a column made of X and digits can end up with one parameter name. A condition
/// list that uses both is refused instead of returning wrong rows on a backend that does not notice.
/// </summary>
public sealed class ParameterNameCollisionTests : IDisposable
{
    private readonly NamePairHarness _h = new();

    public ParameterNameCollisionTests() =>
        _h.Seed(("a", "k1", null), ("b", null, "o"), ("c", "k2", null), ("zz", null, null));

    public void Dispose() => _h.Dispose();

    private static ComparisonFilter Eq(string p, object v) => new(p, QueryOperator.Eq, v);
    private static ComparisonFilter In(string p, params object[] v) => new(p, QueryOperator.In, v.ToList());
    private static LogicalFilter And(params FilterNode[] n) => new(LogicalOperator.And, n);
    private static LogicalFilter Or(params FilterNode[] n) => new(LogicalOperator.Or, n);

    [Fact]
    public async Task A_group_of_a_column_next_to_its_numbered_sibling_is_a_400()
    {
        var many = Enumerable.Range(0, 15).Select(i => (object)$"v{i}").Append("a").ToArray();
        var act = () => _h.QueryAsync(Or(Eq("code1", "k1"), In("code", many)));
        (await act.Should().ThrowAsync<QueryException>()).Which.Message.Should().ContainEquivalentOf("code").And.ContainEquivalentOf("code1");
    }

    [Fact]
    public async Task Plain_conditions_on_a_column_and_its_numbered_sibling_are_a_400()
    {
        var filler = Enumerable.Range(0, 9).Select(i => (FilterNode)Eq("other", $"f{i}")).ToList();
        // position 1 holds code1 and position 11 holds code: both are named 'Code11'
        var act = () => _h.QueryAsync(And([Eq("other", "f"), Eq("code1", "k1"), .. filler, Eq("code", "a")]));
        (await act.Should().ThrowAsync<QueryException>()).Which.Message.Should().ContainEquivalentOf("code1");
    }

    [Fact]
    public async Task A_numbered_sibling_at_the_front_and_the_column_at_position_ten_are_a_400()
    {
        var filler = Enumerable.Range(0, 9).Select(i => (FilterNode)Eq("other", $"f{i}")).ToList();
        var act = () => _h.QueryAsync(And([Eq("code1", "k1"), .. filler, Eq("code", "a")]));
        var ex = (await act.Should().ThrowAsync<QueryException>()).Which;
        ex.Message.Should().Contain("'code'").And.Contain("'code1'");
    }

    [Fact]
    public async Task Inside_one_group_the_tenth_condition_after_the_numbered_sibling_is_a_400()
    {
        var others = Enumerable.Range(0, 9).Select(i => (FilterNode)Eq("other", $"f{i}")).ToList();
        var act = () => _h.QueryAsync(Or([Eq("code1", "k1"), .. others, Eq("code", "a")]));
        await act.Should().ThrowAsync<QueryException>();
    }

    [Fact]
    public async Task A_clash_made_by_the_search_group_does_not_call_itself_a_filter()
    {
        var fields = new[] { "code1" }.Concat(Enumerable.Repeat("other", 9)).Append("code").ToArray();
        var act = () => _h.SearchAsync("a", fields);
        var ex = (await act.Should().ThrowAsync<QueryException>()).Which;
        ex.Message.Should().NotContainEquivalentOf("filter").And.Contain("'code1'");
    }

    [Fact]
    public async Task A_search_over_a_column_and_its_numbered_sibling_is_fine()
    {
        (await _h.SearchAsync("k", ["code", "code1"])).Select(r => r.Code1).Should().BeEquivalentTo("k1", "k2");
        (await _h.SearchAsync("a", ["code", "code1"])).Select(r => r.Code).Should().BeEquivalentTo("a");
    }

    [Fact]
    public async Task Root_conditions_on_a_column_and_its_numbered_sibling_are_fine()
    {
        (await _h.QueryAsync(And(Eq("code", "a"), Eq("code1", "k1")))).Select(r => r.Code).Should().BeEquivalentTo("a");
        (await _h.QueryAsync(And(Eq("code1", "k2"), Eq("code", "c")))).Select(r => r.Code).Should().BeEquivalentTo("c");
    }

    [Fact]
    public void The_name_check_leaves_the_conditions_as_they_were()
    {
        var models = new List<SqlSugar.IConditionalModel>
        {
            new SqlSugar.ConditionalModel { FieldName = "Code", ConditionalType = SqlSugar.ConditionalType.Equal, FieldValue = "[null]" },
            new SqlSugar.ConditionalModel { FieldName = "Code1", ConditionalType = SqlSugar.ConditionalType.Equal, FieldValue = "k1" },
        };
        ParameterNames.EnsureDistinct(_h.Db, () => typeof(QvNamePair), models);
        ((SqlSugar.ConditionalModel)models[0]).FieldValue.Should().Be("[null]");
    }

    // The check relies on SqlSugar repeating a name for such a list; if SqlSugar ever stops, it is dead weight.
    [Fact]
    public void SqlSugar_still_repeats_a_parameter_name_for_a_column_and_its_numbered_sibling()
    {
        var models = new List<SqlSugar.IConditionalModel>
        {
            new SqlSugar.ConditionalModel { FieldName = "Code1", ConditionalType = SqlSugar.ConditionalType.Equal, FieldValue = "k1" },
        };
        for (var i = 0; i < 9; i++)
            models.Add(new SqlSugar.ConditionalModel { FieldName = "Other", ConditionalType = SqlSugar.ConditionalType.Equal, FieldValue = $"f{i}" });
        models.Add(new SqlSugar.ConditionalModel { FieldName = "Code", ConditionalType = SqlSugar.ConditionalType.Equal, FieldValue = "a" });

        var names = _h.Db.Queryable<QvNamePair>().Where(models).ToSql().Value.Select(p => p.ParameterName).ToList();

        names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Should().Contain(g => g.Count() > 1);
    }

    [Fact]
    public async Task Either_column_alone_is_fine_however_many_values_it_takes()
    {
        var many = Enumerable.Range(0, 40).Select(i => (object)$"v{i}").Append("a").Append("c").ToArray();
        (await _h.QueryAsync(In("code", many))).Select(r => r.Code).Should().BeEquivalentTo("a", "c");
        (await _h.QueryAsync(Or(Eq("code", "a"), In("code", many), Eq("other", "o")))).Select(r => r.Code)
            .Should().BeEquivalentTo("a", "b", "c");
        (await _h.QueryAsync(And(In("code1", "k1", "k2"), In("code1", "k1")))).Should().ContainSingle();
    }

    [Fact]
    public async Task Unrelated_column_names_never_collide()
    {
        var many = Enumerable.Range(0, 15).Select(i => (object)$"v{i}").Append("a").ToArray();
        (await _h.QueryAsync(Or(Eq("other", "o"), In("code", many)))).Select(r => r.Code)
            .Should().BeEquivalentTo("a", "b");
    }
}
