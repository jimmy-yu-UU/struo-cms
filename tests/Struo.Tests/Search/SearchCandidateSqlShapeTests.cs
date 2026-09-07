// tests/Struo.Tests/Search/SearchCandidateSqlShapeTests.cs
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Tests.Query;
using Xunit;

namespace Struo.Tests.Search;

/// <summary>
/// The candidate branch of FilterTranslator, observed through the repository on the SQLite pushdown
/// harness: SQL shape (typed `id IN` literals / `IS NULL`, never LIKE) and the three call sites
/// (list, facet, aggregate) all honouring the same candidate set.
/// </summary>
public sealed class SearchCandidateSqlShapeTests : IDisposable
{
    private readonly SubqueryPushdownHarness _h = new();
    private readonly List<string> _sql = [];
    private readonly Guid _alpha1 = Guid.NewGuid(), _alpha2 = Guid.NewGuid(), _beta = Guid.NewGuid();

    public SearchCandidateSqlShapeTests()
    {
        _h.Db.Aop.OnLogExecuting = (sql, _) => _sql.Add(sql);
        _h.Db.Insertable(new[]
        {
            new SqProduct { Id = _alpha1, Name = "alpha one" },
            new SqProduct { Id = _alpha2, Name = "alpha two" },
            new SqProduct { Id = _beta, Name = "beta" },
        }).ExecuteCommand();
        _sql.Clear();
    }

    public void Dispose() => _h.Dispose();

    private static QueryModel Q(string? search, IReadOnlyList<object>? candidates, FilterNode? filter = null) =>
        new QueryModel(null, filter, [], 100, 0, search) { SearchCandidates = candidates };

    private Task<QueryResult> ListAsync(QueryModel q) => _h.Repo.QueryAsync("sqProduct", q, ["name"], null);

    [Fact]
    public async Task Null_candidates_keep_the_like_group()
    {
        var r = await ListAsync(Q("alpha", null));
        r.Total.Should().Be(2);
        _sql.Should().OnlyContain(s => s.Contains("LIKE", StringComparison.OrdinalIgnoreCase));
        _sql.Should().NotContain(s => s.Contains(" IN (", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Candidates_replace_the_like_group_with_typed_id_in()
    {
        var r = await ListAsync(Q("alpha", [_beta]));
        r.Total.Should().Be(1);
        r.Rows.Cast<SqProduct>().Single().Id.Should().Be(_beta);
        _sql.Should().NotContain(s => s.Contains("LIKE", StringComparison.OrdinalIgnoreCase));
        // A single-element candidate set is still translated through the `_in` (QueryOperator.In) leaf,
        // but SqlSugar itself collapses a one-value ConditionalType.In down to `=` rather than
        // `IN (...)` (measured 2026-09-07 — the brief's IN-literal probe used two ids and did not
        // cover this case); either rendering is a typed, unparameterized literal comparing `Id`
        // against exactly `_beta`, so both are accepted here.
        _sql.Should().OnlyContain(s => System.Text.RegularExpressions.Regex.IsMatch(
            s, @"`Id`\s+(=\s*'" + _beta + @"'|IN\s+\('" + _beta + @"'\))"));
    }

    [Fact]
    public async Task Empty_candidates_render_id_is_null_and_match_nothing()
    {
        var r = await ListAsync(Q("alpha", []));
        r.Total.Should().Be(0);
        r.Rows.Should().BeEmpty();
        _sql.Should().OnlyContain(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"`Id`\s+IS\s+NULL"));
        _sql.Should().NotContain(s => s.Contains("LIKE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Candidates_and_filter_are_anded()
    {
        var nameIsAlphaOne = new ComparisonFilter("name", QueryOperator.Eq, "alpha one");
        (await ListAsync(Q("x", [_alpha2], nameIsAlphaOne))).Total.Should().Be(0);
        (await ListAsync(Q("x", [_alpha1], nameIsAlphaOne))).Total.Should().Be(1);
    }

    [Fact]
    public async Task Candidates_do_not_reorder_results()
    {
        var q = new QueryModel(null, null, [new SortField("name", Descending: true)], 100, 0, "x") { SearchCandidates = [_alpha1, _beta, _alpha2] };
        var r = await ListAsync(q);
        r.Rows.Cast<SqProduct>().Select(p => p.Name).Should().Equal("beta", "alpha two", "alpha one");
    }

    [Fact]
    public async Task Facet_and_aggregate_run_on_the_candidate_set()
    {
        var meta = _h.Metadata.GetCollection("sqProduct")!;
        var resolved = FacetPathResolver.Resolve(meta, "name", _h.Graph, _h.Metadata);
        var q = Q("x", [_alpha1, _beta]);

        var buckets = await _h.Repo.FacetAsync(new FacetRequest("sqProduct", q, resolved, ["name"], null, DeletedFilter.Exclude, 50));
        buckets.Select(b => (b.Value, b.Count)).Should().BeEquivalentTo([((object?)"alpha one", 1L), ("beta", 1L)]);

        var spec = new AggregateSpec(new Dictionary<AggregateOp, IReadOnlyList<string>> { [AggregateOp.Count] = ["name"] });
        var agg = await _h.Repo.AggregateAsync("sqProduct", q, spec, ["name"], null, DeletedFilter.Exclude);
        agg.Values[AggregateOp.Count]["name"].Should().Be(2L);
        _sql.Should().NotContain(s => s.Contains("LIKE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task List_still_costs_two_statements_with_candidates()
    {
        await ListAsync(Q("x", [_alpha1]));
        _sql.Should().HaveCount(2, "COUNT + page, exactly as without a provider");
    }
}
