using AwesomeAssertions;
using Struo.Domain.Query;
using Struo.Sample.Blog;
using Xunit;

namespace Struo.Tests.Query;

public sealed partial class PostgresIntegrationTests
{
    // U5: the candidate-id branch renders `id IN ('uuid', …)` as LITERALS (probe 2026-09-07), so these
    // three pin the uuid-column binding, the always-false empty form and the 1000-id statement on a
    // real PostgreSQL — SQLite's loose typing would accept anything here.
    private (Guid A, Guid B, Guid C) SeedThreeCategories()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        _db!.Insertable(new[]
        {
            new Category { Id = a, Name = "u5-a" }, new Category { Id = b, Name = "u5-b" },
            new Category { Id = c, Name = "u5-c" }, new Category { Id = Guid.NewGuid(), Name = "u5-d" },
        }).ExecuteCommand();
        return (a, b, c);
    }

    private static QueryModel WithCandidates(IReadOnlyList<object> ids) =>
        new QueryModel(null, null, [], 100, 0, "ignored-by-the-candidate-branch") { SearchCandidates = ids };

    [Fact]
    public async Task Search_candidates_bind_uuid_ids_in_on_postgres()
    {
        if (!PgConfigured) return;
        var repo = BuildRepo();
        var (a, b, _) = SeedThreeCategories();
        var r = await repo.QueryAsync("category", WithCandidates([a, b]), ["name"], null);
        r.Total.Should().Be(2);
        r.Rows.Cast<Category>().Select(x => x.Id).Should().BeEquivalentTo([a, b]);
    }

    [Fact]
    public async Task Empty_search_candidates_match_nothing_on_postgres()
    {
        if (!PgConfigured) return;
        var repo = BuildRepo();
        SeedThreeCategories();
        var r = await repo.QueryAsync("category", WithCandidates([]), ["name"], null);
        r.Total.Should().Be(0);
        r.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task A_thousand_search_candidates_run_in_one_statement_on_postgres()
    {
        if (!PgConfigured) return;
        var repo = BuildRepo();
        var (a, b, c) = SeedThreeCategories();
        var ids = Enumerable.Range(0, 997).Select(_ => (object)Guid.NewGuid()).Append(a).Append(b).Append(c).ToList();
        var r = await repo.QueryAsync("category", WithCandidates(ids), ["name"], null);
        r.Total.Should().Be(3);
    }
}
