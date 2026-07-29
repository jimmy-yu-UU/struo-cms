using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

// A single operation must not be able to amplify DB load by repeating an expensive list
// field under many aliases (there is no rate limiting in front of /graphql). The cost analyzer
// (AddCostAnalyzer in GraphQlServiceCollectionExtensions) accrues field cost per alias and rejects
// the operation before any resolver runs. HotChocolate 16.4.0 has no dedicated alias-count rule —
// cost analysis IS the alias-amplification defense (each alias accrues its own field cost).
[Collection("ApiIntegration")]
public class GraphQlCostAnalysisTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Alias_bomb_is_rejected_by_cost_analyzer()
    {
        const int aliases = 50;
        var body = string.Concat(Enumerable.Range(0, aliases)
            .Select(i => $"a{i}: articles {{ items {{ id }} }} "));
        var query = "{ " + body + "}";

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue(
            $"expected the cost analyzer to reject a {aliases}-alias amplification; full response: {json}");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        // Prove it was rejected specifically by cost analysis (HC0047 = max field cost exceeded),
        // not some unrelated error, and that the measured cost exceeds the configured limit.
        var ext = errors[0].GetProperty("extensions");
        ext.GetProperty("code").GetString().Should().Be("HC0047");
        ext.GetProperty("fieldCost").GetDouble().Should().BeGreaterThan(
            ext.GetProperty("maxFieldCost").GetDouble(),
            $"the {aliases}-alias amplification must exceed the configured MaxFieldCost; full response: {json}");
        // The operation must not have executed: `data` is absent or null.
        if (doc.RootElement.TryGetProperty("data", out var data))
            data.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Normal_multi_root_query_passes_cost_analyzer()
    {
        // The legitimate 3-root query used by GraphQlConcurrencySmokeTests must stay under the limit.
        // Authenticated (super-admin) so RBAC lets every root through — this isolates the assertion
        // to the cost analyzer rather than per-collection read grants.
        const string query =
            "{ articles { items { id } } categories { items { id } } tags { items { id } } }";
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse(
            $"a legitimate multi-root query must not be rejected by the cost analyzer; full response: {json}");
    }
}
