// tests/Struo.Tests/Security/QueryValueInjection/FilterShapeSetTests.cs
using AwesomeAssertions;
using Struo.Domain.Query;
using Struo.Tests.Query;
using Xunit;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>`_in`/`_nin` on text and number columns in every filter position the translator builds.</summary>
public sealed class FilterShapeSetTests : IDisposable
{
    private readonly SubqueryPushdownHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static ComparisonFilter In(string p, params object[] v) => new(p, QueryOperator.In, v.ToList());
    private static ComparisonFilter Nin(string p, params object[] v) => new(p, QueryOperator.Nin, v.ToList());
    private static ComparisonFilter Eq(string p, object v) => new(p, QueryOperator.Eq, v);
    private static LogicalFilter And(params FilterNode[] n) => new(LogicalOperator.And, n);
    private static LogicalFilter Or(params FilterNode[] n) => new(LogicalOperator.Or, n);
    private static RelationPredicateFilter Some(string rel, FilterNode inner) => new(rel, RelationQuantifier.Some, inner);
    private static RelationPredicateFilter None(string rel, FilterNode inner) => new(rel, RelationQuantifier.None, inner);

    private async Task<List<string>> NamesAsync(FilterNode? filter)
    {
        var ids = await _h.QueryIdsAsync(filter);
        var all = _h.Db.Queryable<SqProduct>().ClearFilter<Struo.Domain.Auditing.ISoftDeletable>().ToList();
        return all.Where(p => ids.Contains(p.Id)).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private async Task<List<string>> PropertyCodesAsync(FilterNode? filter)
    {
        var r = await _h.Repo.QueryAsync("sqProperty", new QueryModel(null, filter, [], 100, 0, null), [], null);
        return r.Rows.Cast<SqProperty>().Select(p => $"{p.Code}:{p.ValueNum}").OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public async Task Root_in_and_nin_on_a_text_column()
    {
        _h.SeedEav();
        (await NamesAsync(In("name", "cross", "same"))).Should().Equal("cross", "same");
        (await NamesAsync(Nin("name", "cross", "same"))).Should().Equal("bare", "nocat");
        (await NamesAsync(And(In("name", "cross", "same", "bare"), Nin("name", "same")))).Should().Equal("bare", "cross");
    }

    [Fact]
    public async Task Empty_lists_keep_their_meaning_next_to_other_conditions()
    {
        _h.SeedEav();
        (await NamesAsync(In("name"))).Should().BeEmpty();
        (await NamesAsync(And(Eq("name", "same"), In("name")))).Should().BeEmpty();
        (await NamesAsync(And(In("name"), Eq("name", "same")))).Should().BeEmpty();
        (await NamesAsync(And(Eq("name", "same"), Nin("name")))).Should().Equal("same");
        (await NamesAsync(Nin("name"))).Should().Equal("bare", "cross", "nocat", "same");
    }

    [Fact]
    public async Task Or_flattens_a_text_in_into_the_group_and_keeps_the_soft_delete_filter_on_the_whole_condition()
    {
        _h.SeedEav();
        (await NamesAsync(Or(In("name", "cross"), Eq("name", "same")))).Should().Equal("cross", "same");
        (await NamesAsync(Or(Eq("name", "bare"), In("name", "cross", "nocat")))).Should().Equal("bare", "cross", "nocat");
        (await NamesAsync(And(Eq("name", "bare"), Or(In("name", "bare", "same"))))).Should().Equal("bare");

        _h.Db.Updateable<SqProduct>().SetColumns(p => p.DeletedAt == DateTime.UtcNow).Where(p => p.Name == "cross").ExecuteCommand();
        (await NamesAsync(Or(In("name", "cross", "same"), Eq("name", "bare")))).Should().Equal("bare", "same");
        (await NamesAsync(Or(Eq("name", "bare"), In("name", "cross", "same")))).Should().Equal("bare", "same");
    }

    [Fact]
    public async Task Or_accepts_a_number_in_beside_a_text_in()
    {
        _h.SeedEav();
        var rows = await PropertyCodesAsync(Or(In("code", "vds-v"), In("valueNum", 100L)));
        rows.Should().Equal("ptot-w:100", "vds-v:20", "vds-v:80");
        (await PropertyCodesAsync(Or(Nin("valueNum", 100L, 1L), Eq("code", "ptot-w")))).Should().Equal("ptot-w:100", "vds-v:20", "vds-v:80");
    }

    [Fact]
    public async Task Or_with_an_empty_list_drops_that_term_and_an_empty_nin_matches_everything()
    {
        _h.SeedEav();
        (await NamesAsync(Or(In("name"), Eq("name", "same")))).Should().Equal("same");
        (await NamesAsync(Or(In("name")))).Should().BeEmpty();
        (await NamesAsync(Or(In("name"), In("name")))).Should().BeEmpty();
        (await PropertyCodesAsync(Or(Nin("valueNum"), Eq("code", "zz")))).Should().HaveCount(4);
        (await PropertyCodesAsync(Or(In("valueNum"), Eq("code", "zz")))).Should().BeEmpty();
    }

    [Fact]
    public async Task Or_with_a_text_nin_is_refused_and_says_where_it_can_go()
    {
        _h.SeedEav();
        var act = () => NamesAsync(Or(Eq("name", "same"), Nin("name", "cross")));
        (await act.Should().ThrowAsync<QueryException>()).Which.Message
            .Should().Contain("_nin").And.Contain("name").And.Contain("_and");
        var empty = () => NamesAsync(Or(Nin("name")));
        await empty.Should().ThrowAsync<QueryException>();
    }

    [Fact]
    public async Task Relation_predicates_and_dotted_paths_take_text_sets()
    {
        _h.SeedEav();
        (await NamesAsync(Some("properties", In("code", "vds-v", "o'neil")))).Should().Equal("cross", "nocat", "same");
        (await NamesAsync(Some("properties", Nin("code", "vds-v")))).Should().Equal("cross", "nocat");
        (await NamesAsync(None("properties", Nin("code", "vds-v")))).Should().Equal("bare", "same");
        (await NamesAsync(None("properties", In("code", "vds-v", "o'neil")))).Should().Equal("bare");
        (await NamesAsync(In("properties.code", "ptot-w", "o'neil"))).Should().Equal("cross", "nocat");
        (await NamesAsync(Nin("category.name", "Tech"))).Should().Equal("same");
        (await NamesAsync(In("category.name", "Tech", "Archive"))).Should().Equal("bare", "cross", "same");
        (await NamesAsync(Or(In("properties.code", "ptot-w"), Eq("name", "bare")))).Should().Equal("bare", "cross");
    }

    [Fact]
    public async Task Junction_payload_conditions_take_text_sets()
    {
        _h.SeedEav();
        (await NamesAsync(Some("labels", And(Eq("name", "Guide"), In("_junction.note", "hero", "x'y"))))).Should().Equal("cross");
        (await NamesAsync(Some("labels", And(Eq("name", "Guide"), Nin("_junction.note", "hero"))))).Should().Equal("same");
        (await NamesAsync(Some("labels", In("name", "Guide", "Misc")))).Should().Equal("cross", "same");
    }

    [Fact]
    public async Task Deep_extra_filters_take_text_sets()
    {
        var (cross, same, _, _) = _h.SeedEav();
        var rows = await _h.Repo.QueryWhereInFilteredAsync(
            "sqProperty", "productId", [cross, same], In("code", "vds-v"), null);
        rows.Cast<SqProperty>().Select(p => p.ValueNum).Order().Should().Equal(20, 80);
        var nin = await _h.Repo.QueryWhereInFilteredAsync(
            "sqProperty", "productId", [cross, same], Nin("code", "vds-v"), null);
        nin.Cast<SqProperty>().Select(p => p.Code).Should().Equal("ptot-w");
        var none = await _h.Repo.QueryWhereInFilteredAsync(
            "sqProperty", "productId", [cross, same], In("code"), null);
        none.Should().BeEmpty();
    }

    [Fact]
    public async Task Facets_and_aggregates_take_text_sets()
    {
        _h.SeedEav();
        (await _h.FacetAsync("category", In("name", "cross", "same", "o'neil"))).Should().HaveCount(2);
        (await _h.FacetAsync("category", Nin("name", "cross", "same"))).Should().HaveCount(1);
        (await _h.FacetAsync("category", In("name", "zz"))).Should().BeEmpty();

        var spec = new AggregateSpec(new Dictionary<AggregateOp, IReadOnlyList<string>>
        {
            [AggregateOp.Count] = ["valueNum"], [AggregateOp.Sum] = ["valueNum"],
        });
        var counted = await _h.AggregateAsync("sqProperty", spec, In("code", "vds-v"));
        counted.Values[AggregateOp.Count]["valueNum"].Should().Be(2L);
        Convert.ToInt64(counted.Values[AggregateOp.Sum]["valueNum"]).Should().Be(100);
        var excluded = await _h.AggregateAsync("sqProperty", spec, Nin("code", "vds-v"));
        excluded.Values[AggregateOp.Count]["valueNum"].Should().Be(2L);
    }

    [Fact]
    public async Task The_search_group_and_a_text_set_combine()
    {
        _h.SeedEav();
        var q = new QueryModel(null, In("name", "cross", "same", "bare"), [], 100, 0, "ss");
        var r = await _h.Repo.QueryAsync("sqProduct", q, ["name"], null);
        r.Rows.Cast<SqProduct>().Select(p => p.Name).Should().Equal("cross");
    }
}
