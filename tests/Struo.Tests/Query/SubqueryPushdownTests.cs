// tests/Struo.Tests/Query/SubqueryPushdownTests.cs
using AwesomeAssertions;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class SubqueryPushdownTests : IDisposable
{
    private readonly SubqueryPushdownHarness _h = new();
    public void Dispose() => _h.Dispose();

    private static ComparisonFilter Eq(string p, object v) => new(p, QueryOperator.Eq, v);
    private static ComparisonFilter Gte(string p, object v) => new(p, QueryOperator.Gte, v);
    private static LogicalFilter And(params FilterNode[] n) => new(LogicalOperator.And, n);
    private static LogicalFilter Or(params FilterNode[] n) => new(LogicalOperator.Or, n);
    private static RelationPredicateFilter Some(string rel, FilterNode inner) => new(rel, RelationQuantifier.Some, inner);
    private static RelationPredicateFilter None(string rel, FilterNode inner) => new(rel, RelationQuantifier.None, inner);

    // ── A1: the two assertions that must coexist ───────────────────────────
    [Fact]
    public async Task Dotted_path_conditions_are_each_exists_and_match_across_rows()
    {
        var (cross, same, _, _) = _h.SeedEav();
        var ids = await _h.QueryIdsAsync(And(Eq("properties.code", "vds-v"), Gte("properties.valueNum", 60)));
        ids.Should().BeEquivalentTo([cross, same]);
    }

    [Fact]
    public async Task Some_binds_conditions_to_one_related_row()
    {
        var (_, same, _, _) = _h.SeedEav();
        var ids = await _h.QueryIdsAsync(Some("properties", And(Eq("code", "vds-v"), Gte("valueNum", 60))));
        ids.Should().BeEquivalentTo([same]);
    }

    // ── _none ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task None_on_o2m_includes_parents_with_no_related_rows()
    {
        var (_, _, noProps, noCat) = _h.SeedEav();
        var ids = await _h.QueryIdsAsync(None("properties", Eq("code", "vds-v")));
        ids.Should().BeEquivalentTo([noProps, noCat]);
    }

    [Fact]
    public async Task None_on_m2o_includes_null_foreign_keys()
    {
        var (_, same, _, noCat) = _h.SeedEav();
        var ids = await _h.QueryIdsAsync(None("category", Eq("name", "Tech")));
        ids.Should().BeEquivalentTo([same, noCat]);
    }

    [Fact]
    public async Task None_on_m2m_excludes_only_linked_parents()
    {
        var (_, _, noProps, noCat) = _h.SeedEav();
        var ids = await _h.QueryIdsAsync(None("labels", Eq("name", "Guide")));
        ids.Should().BeEquivalentTo([noProps, noCat]);
    }

    // ── three kinds + composition ─────────────────────────────────────────
    [Fact]
    public async Task Some_on_m2o_equals_dotted_path()
    {
        var (cross, _, noProps, _) = _h.SeedEav();
        (await _h.QueryIdsAsync(Some("category", Eq("name", "Tech")))).Should().BeEquivalentTo([cross, noProps]);
        (await _h.QueryIdsAsync(Eq("category.name", "Tech"))).Should().BeEquivalentTo([cross, noProps]);
    }

    [Fact]
    public async Task Some_on_m2m_with_junction_field_binds_target_and_junction_to_one_link()
    {
        var (cross, same, _, _) = _h.SeedEav();
        // Guide+hero exists only on `cross`; `same` has Guide+plain and Misc+hero (cross-row).
        var ids = await _h.QueryIdsAsync(Some("labels", And(Eq("name", "Guide"), Eq("_junction.note", "hero"))));
        ids.Should().BeEquivalentTo([cross]);
        var each = await _h.QueryIdsAsync(And(Eq("labels.name", "Guide"), Eq("labels._junction.note", "hero")));
        each.Should().BeEquivalentTo([cross, same]);
    }

    [Fact]
    public async Task Junction_only_dotted_path_works_without_target_conditions()
    {
        var (_, same, _, _) = _h.SeedEav();
        (await _h.QueryIdsAsync(Eq("labels._junction.note", "plain"))).Should().BeEquivalentTo([same]);
    }

    [Fact]
    public async Task Junction_condition_or_ed_with_target_condition_inside_a_predicate_is_rejected()
    {
        _h.SeedEav();
        var act = async () => await _h.QueryIdsAsync(
            Some("labels", Or(Eq("_junction.note", "hero"), Eq("name", "Guide"))));
        await act.Should().ThrowAsync<QueryException>();
    }

    [Fact]
    public async Task Predicate_inside_or_group_composes_with_scalar()
    {
        var (cross, _, _, noCat) = _h.SeedEav();
        var ids = await _h.QueryIdsAsync(Or(Eq("name", "nocat"), Some("properties", Gte("valueNum", 100))));
        ids.Should().BeEquivalentTo([noCat, cross]);
    }

    [Fact]
    public async Task Or_over_two_relation_conditions_composes()
    {
        var (cross, same, noProps, noCat) = _h.SeedEav();
        var some = await _h.QueryIdsAsync(Or(Some("labels", Eq("name", "Guide")), Eq("category.name", "Archive")));
        some.Should().BeEquivalentTo([cross, same]);
        var none = await _h.QueryIdsAsync(Or(None("labels", Eq("name", "Guide")), Eq("category.name", "Archive")));
        none.Should().BeEquivalentTo([same, noProps, noCat]);
    }

    [Fact]
    public async Task Or_over_three_children_mixing_scalar_and_two_subqueries()
    {
        var (cross, same, _, noCat) = _h.SeedEav();
        var ids = await _h.QueryIdsAsync(Or(
            Eq("name", "nocat"), Some("properties", Gte("valueNum", 100)), Eq("labels._junction.note", "plain")));
        ids.Should().BeEquivalentTo([noCat, cross, same]);
    }

    [Fact]
    public async Task Or_whose_children_are_all_subqueries()
    {
        var (cross, same, noProps, _) = _h.SeedEav();
        var ids = await _h.QueryIdsAsync(Or(Eq("category.name", "Tech"), Eq("labels.name", "Misc")));
        ids.Should().BeEquivalentTo([cross, noProps, same]);
    }

    [Fact]
    public async Task Dotted_path_inside_a_predicate_inner()
    {
        var (_, same, _, _) = _h.SeedEav();
        var ids = await _h.QueryIdsAsync(Some("properties", Eq("product.category.name", "Archive")));
        ids.Should().BeEquivalentTo([same]);
    }

    [Fact]
    public async Task Multi_hop_dotted_path_through_o2m_and_m2o()
    {
        var (cross, _, _, _) = _h.SeedEav();
        (await _h.QueryIdsAsync(Eq("properties.product.category.name", "Tech"))).Should().BeEquivalentTo([cross]);
    }

    // ── robustness ────────────────────────────────────────────────────────
    [Fact]
    public async Task Apostrophe_in_inner_value_is_escaped()
    {
        var (_, _, _, noCat) = _h.SeedEav();
        (await _h.QueryIdsAsync(Some("properties", Eq("code", "o'neil")))).Should().BeEquivalentTo([noCat]);
    }

    [Fact]
    public async Task A_list_query_with_many_relation_conditions_is_two_sql_statements()
    {
        _h.SeedEav();
        _h.ResetSqlCount();
        await _h.QueryIdsAsync(And(
            Eq("category.name", "Tech"), Some("properties", Gte("valueNum", 1)), None("labels", Eq("name", "zzz")),
            Or(Eq("name", "x"), Eq("labels._junction.note", "hero")), Eq("properties.code", "vds-v")));
        _h.SqlStatements.Should().Be(2, "COUNT + SELECT, no id materialization round trips");
    }

    [Fact]
    public async Task Wide_related_set_beyond_the_old_cap_is_not_refused()
    {
        var (cross, _, _, _) = _h.SeedEav();
        var many = Enumerable.Range(0, 5001).Select(i => new SqProperty { Id = Guid.NewGuid(), ProductId = cross, Code = $"bulk{i}", ValueNum = i }).ToList();
        _h.Db.Insertable(many).ExecuteCommand();
        var ids = await _h.QueryIdsAsync(new ComparisonFilter("properties.code", QueryOperator.Contains, "bulk"));
        ids.Should().BeEquivalentTo([cross]);
    }

    [Fact]
    public async Task Soft_deleted_related_rows_stay_excluded_even_when_outer_includes_trash()
    {
        var (_, same, _, _) = _h.SeedEav();
        // Trash product `same`; a property-side path back through property.product must not see it even
        // when the OUTER query includes trash (spec R2: the subquery keeps the soft-delete floor).
        _h.Db.Updateable<SqProduct>().SetColumns(p => new SqProduct { DeletedAt = DateTime.UtcNow }).Where(p => p.Id == same).ExecuteCommand();
        var withTrash = await _h.QueryIdsAsync(Eq("properties.product.name", "same"), deleted: DeletedFilter.With);
        withTrash.Should().BeEmpty();
    }

    // ── Investigation: filter + search must combine as AND, relation predicates included ──────────
    [Fact]
    public async Task Scalar_relation_filter_and_search_combine_as_and()
    {
        var (cross, _, noProps, _) = _h.SeedEav();
        // Both cross and noProps match the filter (category = Tech); only cross's name matches the search.
        var ids = await _h.QueryIdsAsync(Eq("category.name", "Tech"), search: "cross");
        ids.Should().BeEquivalentTo([cross]);
        noProps.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Some_predicate_filter_and_search_combine_as_and()
    {
        var (cross, same, _, _) = _h.SeedEav();
        // Both cross and same match the filter (a property with valueNum >= 50); only same's name matches the search.
        var ids = await _h.QueryIdsAsync(Some("properties", Gte("valueNum", 50)), search: "same");
        ids.Should().BeEquivalentTo([same]);
        cross.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Own_field_filter_and_search_combine_as_and()
    {
        _h.SeedEav();
        // Filter matches "bare" only; search matches "cross" only - the AND of the two must be empty.
        var ids = await _h.QueryIdsAsync(Eq("name", "bare"), search: "cross");
        ids.Should().BeEmpty();
    }
}
