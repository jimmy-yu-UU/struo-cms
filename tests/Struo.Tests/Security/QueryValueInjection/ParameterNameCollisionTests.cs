// tests/Struo.Tests/Security/QueryValueInjection/ParameterNameCollisionTests.cs
using AwesomeAssertions;
using Struo.Domain.Query;
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
