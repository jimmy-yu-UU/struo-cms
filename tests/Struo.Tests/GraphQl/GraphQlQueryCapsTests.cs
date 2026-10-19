using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

[Collection("ApiIntegration")]
public class GraphQlQueryCapsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task<(JsonElement Root, string Raw)> Run(string query)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var resp = await c.PostAsJsonAsync("/graphql", new { query });
        var raw = await resp.Content.ReadAsStringAsync();
        return (JsonDocument.Parse(raw).RootElement, raw);
    }

    // Relations per sample type: (field, target type). Leaf scalar per type: title/name.
    private static readonly Dictionary<string, (string Field, string Target)[]> Graph = new()
    {
        ["article"] = [("category", "category"), ("tags", "tag")],
        ["category"] = [("parent", "category"), ("children", "category"), ("articles", "article")],
        ["tag"] = [],
    };

    // Article selection holding exactly `nodes` relation nodes, kept acyclic enough (a field repeats
    // at most twice per path) for the schema's cycle-depth rule.
    private static string Selection(int nodes)
    {
        var remaining = nodes;
        return Fill("article", ref remaining, depthLeft: 6, []);
    }

    private static string Fill(string type, ref int remaining, int depthLeft, Dictionary<string, int> path)
    {
        var parts = new List<string> { type == "article" ? "title" : "name" };
        foreach (var (field, target) in Graph[type])
        {
            if (remaining == 0 || depthLeft == 0 || path.GetValueOrDefault(field) >= 2) continue;
            remaining--;
            var next = new Dictionary<string, int>(path) { [field] = path.GetValueOrDefault(field) + 1 };
            parts.Add($"{field} {{ {Fill(target, ref remaining, depthLeft - 1, next)} }}");
        }
        return string.Join(" ", parts);
    }

    [Fact]
    public async Task Six_sort_fields_are_rejected()
    {
        var (root, raw) = await Run("{ categories(sort: [\"name\",\"-name\",\"createdAt\",\"-createdAt\",\"id\",\"-id\"]) { total } }");
        root.TryGetProperty("errors", out var errors).Should().BeTrue(raw);
        raw.Should().Contain("Too many sort fields");
        errors.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Five_sort_fields_are_accepted()
    {
        var (root, raw) = await Run("{ categories(sort: [\"name\",\"-name\",\"createdAt\",\"-createdAt\",\"id\"]) { total } }");
        root.TryGetProperty("errors", out _).Should().BeFalse(raw);
    }

    [Fact]
    public async Task Twenty_one_relation_nodes_are_rejected()
    {
        var (root, raw) = await Run($"{{ articles {{ items {{ {Selection(21)} }} }} }}");
        root.TryGetProperty("errors", out _).Should().BeTrue(raw);
        raw.Should().Contain("relations");
    }

    [Fact]
    public async Task Twenty_relation_nodes_are_accepted()
    {
        var (root, raw) = await Run($"{{ articles {{ items {{ {Selection(20)} }} }} }}");
        root.TryGetProperty("errors", out _).Should().BeFalse(raw);
    }
}
