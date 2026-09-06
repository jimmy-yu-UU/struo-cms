using AwesomeAssertions;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class AggregateQueriesTests : IDisposable
{
    private readonly SubqueryPushdownHarness _h = new();
    public void Dispose() { _h.Dispose(); GC.SuppressFinalize(this); }

    private static AggregateSpec Spec(params (AggregateOp Op, string[] Fields)[] parts) =>
        new(parts.ToDictionary(p => p.Op, p => (IReadOnlyList<string>)p.Fields));

    [Fact]
    public async Task All_five_ops_over_an_int_field_in_one_statement()
    {
        _h.SeedEav();
        _h.ResetSqlCount();
        var r = await _h.AggregateAsync("sqProperty", Spec(
            (AggregateOp.Count, ["valueNum"]), (AggregateOp.Sum, ["valueNum"]), (AggregateOp.Min, ["valueNum"]),
            (AggregateOp.Max, ["valueNum"]), (AggregateOp.Avg, ["valueNum"])));
        _h.SqlStatements.Should().Be(1);
        r.Values[AggregateOp.Count]["valueNum"].Should().Be(4L);
        r.Values[AggregateOp.Sum]["valueNum"].Should().BeOfType<long>().And.Be(201L);
        r.Values[AggregateOp.Min]["valueNum"].Should().BeOfType<int>().And.Be(1);
        r.Values[AggregateOp.Max]["valueNum"].Should().BeOfType<int>().And.Be(100);
        r.Values[AggregateOp.Avg]["valueNum"].Should().BeOfType<double>().And.Be(50.25);
    }

    [Fact]
    public async Task Filter_and_search_scope_the_aggregate()
    {
        _h.SeedEav();
        var r = await _h.AggregateAsync("sqProperty", Spec((AggregateOp.Sum, ["valueNum"])), new ComparisonFilter("code", QueryOperator.Eq, "vds-v"));
        r.Values[AggregateOp.Sum]["valueNum"].Should().Be(100L);
    }

    [Fact]
    public async Task Empty_set_gives_null_for_sum_min_max_avg_and_zero_for_count()
    {
        _h.SeedEav();
        var r = await _h.AggregateAsync("sqProperty", Spec((AggregateOp.Sum, ["valueNum"]), (AggregateOp.Avg, ["valueNum"]), (AggregateOp.Count, ["valueNum"])),
            new ComparisonFilter("code", QueryOperator.Eq, "none"));
        r.Values[AggregateOp.Sum]["valueNum"].Should().BeNull();
        r.Values[AggregateOp.Avg]["valueNum"].Should().BeNull();
        r.Values[AggregateOp.Count]["valueNum"].Should().Be(0L);
    }

    [Fact]
    public async Task Count_skips_nulls_and_datetime_min_max_come_back_typed()
    {
        _h.SeedEav();
        var r = await _h.AggregateAsync("sqProduct", Spec((AggregateOp.Count, ["categoryId"]), (AggregateOp.Min, ["createdAt"]), (AggregateOp.Max, ["createdAt"])));
        r.Values[AggregateOp.Count]["categoryId"].Should().Be(3L);
        r.Values[AggregateOp.Min]["createdAt"].Should().BeOfType<DateTime>();
        r.Values[AggregateOp.Max]["createdAt"].Should().BeOfType<DateTime>();
    }

    [Fact]
    public async Task Only_requested_ops_appear_and_field_keys_keep_the_requested_spelling()
    {
        _h.SeedEav();
        var r = await _h.AggregateAsync("sqProperty", Spec((AggregateOp.Max, ["valueNum"])));
        r.Values.Keys.Should().Equal(AggregateOp.Max);
        r.Values[AggregateOp.Max].Keys.Should().Equal("valueNum");
    }

    [Fact]
    public async Task More_than_ten_slots_run_in_a_second_statement()
    {
        _h.SeedEav();
        var fields = Enumerable.Repeat("valueNum", 6).ToArray();
        _h.ResetSqlCount();
        await _h.AggregateAsync("sqProperty", Spec((AggregateOp.Count, fields), (AggregateOp.Max, fields)));
        _h.SqlStatements.Should().Be(2);
    }

    [Fact]
    public async Task Deleted_mode_applies_to_the_aggregate_root()
    {
        var (_, _, bare, _) = _h.SeedEav();
        await _h.Repo.SoftDeleteAsync("sqProduct", bare.ToString(), DateTime.UtcNow, null);
        (await _h.AggregateAsync("sqProduct", Spec((AggregateOp.Count, ["name"])))).Values[AggregateOp.Count]["name"].Should().Be(3L);
        (await _h.AggregateAsync("sqProduct", Spec((AggregateOp.Count, ["name"])), deleted: DeletedFilter.With)).Values[AggregateOp.Count]["name"].Should().Be(4L);
        (await _h.AggregateAsync("sqProduct", Spec((AggregateOp.Count, ["name"])), deleted: DeletedFilter.Only)).Values[AggregateOp.Count]["name"].Should().Be(1L);
    }
}
