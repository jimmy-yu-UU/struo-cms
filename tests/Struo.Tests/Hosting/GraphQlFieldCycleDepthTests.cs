// tests/Struo.Tests/Hosting/GraphQlFieldCycleDepthTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// How often one field may repeat along a path is <c>GraphQl:MaxFieldCycleDepth</c> (default 3): the
/// last allowed repetition passes validation and the next one is refused with <c>HC0087</c>.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlFieldCycleDepthTests
{
    private const string Refused = "HC0087";

    // These queries are deep and wide; the depth and cost limits are not what is measured here.
    private static void RaiseOtherLimits(Dictionary<string, string?> settings)
    {
        settings["GraphQl:MaxExecutionDepth"] = "60";
        settings["GraphQl:MaxFieldCost"] = "100000";
        settings["GraphQl:MaxTypeCost"] = "100000";
    }

    // fxCategories { items { parent { parent { ... name } } } }: one field of one type, repeated.
    private static string SelfReferencing(int repetitions) =>
        "{ fxCategories { items { " + string.Concat(Enumerable.Repeat("parent { ", repetitions))
        + "name" + string.Concat(Enumerable.Repeat(" }", repetitions)) + " } } }";

    // category.articles.category.articles ...: two fields of two types, each repeated once per round.
    private static string ThroughTwoTypes(int rounds)
    {
        var open = string.Concat(Enumerable.Repeat("articles { category { ", rounds));
        var close = string.Concat(Enumerable.Repeat("} } ", rounds));
        return "{ fxCategories { items { " + open + "name " + close + "} } }";
    }

    private static async Task<(JsonElement Root, string Text)> PostAsync(ForkHost host, string query)
    {
        var response = await host.CreateClient().PostAsJsonAsync("/graphql", new { query });
        var text = await response.Content.ReadAsStringAsync();
        return (JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private static string[] Codes(JsonElement root) =>
        !root.TryGetProperty("errors", out var errors)
            ? []
            : [.. errors.EnumerateArray()
                .Select(e => e.TryGetProperty("extensions", out var x) && x.TryGetProperty("code", out var c)
                    ? c.GetString() ?? "" : "")];

    private static async Task AssertBoundaryAsync(
        Func<int, string> build, int allowed, Action<Dictionary<string, string?>>? mutate = null)
    {
        await using var host = await ForkHost.StartAsync(mutate: s =>
        {
            RaiseOtherLimits(s);
            mutate?.Invoke(s);
        });

        var (passing, passingText) = await PostAsync(host, build(allowed));
        Codes(passing).Should().NotContain(Refused, passingText);
        passing.TryGetProperty("data", out _).Should().BeTrue(passingText);

        var (refused, refusedText) = await PostAsync(host, build(allowed + 1));
        Codes(refused).Should().Contain(Refused, refusedText);
        refused.GetProperty("errors")[0].GetProperty("message").GetString()
            .Should().Be("Maximum allowed coordinate cycle depth was exceeded.", refusedText);
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public Task By_default_a_field_may_repeat_three_times_along_a_path() =>
        AssertBoundaryAsync(SelfReferencing, allowed: 3);

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public Task By_default_two_fields_repeated_through_two_types_stop_at_three_rounds() =>
        AssertBoundaryAsync(ThroughTwoTypes, allowed: 3);

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public Task A_configured_value_moves_the_boundary() =>
        AssertBoundaryAsync(SelfReferencing, allowed: 5, s => s["GraphQl:MaxFieldCycleDepth"] = "5");

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public Task A_configured_value_moves_the_boundary_through_two_types() =>
        AssertBoundaryAsync(ThroughTwoTypes, allowed: 4, s => s["GraphQl:MaxFieldCycleDepth"] = "4");

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public Task A_lower_configured_value_tightens_the_boundary() =>
        AssertBoundaryAsync(SelfReferencing, allowed: 1, s => s["GraphQl:MaxFieldCycleDepth"] = "1");
}
