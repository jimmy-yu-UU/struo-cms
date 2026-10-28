using AwesomeAssertions;
using Struo.Domain.Query;
using Struo.Tests.Fixtures;
using Xunit;

namespace Struo.Tests.Query;

public sealed partial class LiveRepositoryTests
{
    // U5: the candidate-id branch renders `id IN ('uuid', …)` as LITERALS, so these
    // three pin the uuid-column binding, the always-false empty form and the 1000-id statement on a
    // real PostgreSQL — SQLite's loose typing would accept anything here.
    private (Guid A, Guid B, Guid C) SeedThreeCategories()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        _db!.Insertable(new[]
        {
            new FxCategory { Id = a, Name = "u5-a" }, new FxCategory { Id = b, Name = "u5-b" },
            new FxCategory { Id = c, Name = "u5-c" }, new FxCategory { Id = Guid.NewGuid(), Name = "u5-d" },
        }).ExecuteCommand();
        return (a, b, c);
    }

    private static QueryModel WithCandidates(IReadOnlyList<object> ids, string search = "ignored-by-the-candidate-branch") =>
        new QueryModel(null, null, [], 100, 0, search) { SearchCandidates = ids };

    [Theory, MemberData(nameof(Backends))]
    public async Task Search_candidates_bind_uuid_ids_in(string backend)
    {
        if (!Use(backend)) return;
        var repo = BuildRepo();
        var (a, b, _) = SeedThreeCategories();
        var r = await repo.QueryAsync("fxCategory", WithCandidates([a, b]), ["name"], null);
        r.Total.Should().Be(2);
        r.Rows.Cast<FxCategory>().Select(x => x.Id).Should().BeEquivalentTo([a, b]);
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Empty_search_candidates_match_nothing(string backend)
    {
        if (!Use(backend)) return;
        var repo = BuildRepo();
        SeedThreeCategories();
        // "u5-" collides via LIKE '%u5-%' with all four seeded names, so if a regression ever treated
        // an empty candidate list like "no candidates" (falling back to the LIKE search on this term),
        // this would come back with 4 rows instead of 0 — proving the assertion below actually
        // exercises the empty-candidates `id IS NULL` branch, not an accidentally-unmatched search term.
        var r = await repo.QueryAsync("fxCategory", WithCandidates([], search: "u5-"), ["name"], null);
        r.Total.Should().Be(0);
        r.Rows.Should().BeEmpty();
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task A_thousand_search_candidates_run_in_one_statement(string backend)
    {
        if (!Use(backend)) return;
        var repo = BuildRepo();
        var (a, b, c) = SeedThreeCategories();
        var ids = Enumerable.Range(0, 997).Select(_ => (object)Guid.NewGuid()).Append(a).Append(b).Append(c).ToList();
        var r = await repo.QueryAsync("fxCategory", WithCandidates(ids), ["name"], null);
        r.Total.Should().Be(3);
        r.Rows.Cast<FxCategory>().Select(x => x.Id).Should().BeEquivalentTo([a, b, c]);
    }
}
