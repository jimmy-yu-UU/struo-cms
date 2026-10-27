// tests/Struo.Tests/Hosting/GraphQlLimitOptionsTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>The GraphQL depth and cost limits are config-bound (<c>GraphQl:*</c>), validated at startup, and applied to the schema.</summary>
[Collection("ApiIntegration")]
public class GraphQlLimitOptionsTests
{
    [SqliteOnlyTheory("starts a full host on a SQLite temp database")]
    [InlineData("MaxExecutionDepth", "0")]
    [InlineData("MaxExecutionDepth", "-3")]
    [InlineData("MaxFieldCost", "0")]
    [InlineData("MaxFieldCost", "-1")]
    [InlineData("MaxTypeCost", "0")]
    [InlineData("MaxTypeCost", "-1")]
    public async Task A_limit_that_is_not_positive_fails_startup_naming_the_key(string key, string value)
    {
        var error = await ForkHost.StartupErrorAsync(mutate: s => s[$"GraphQl:{key}"] = value);

        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull(error?.ToString());
        string.Join(" ", ove!.Failures).Should().Contain($"GraphQl:{key}");
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task The_depth_limit_comes_from_configuration()
    {
        await using var host = await ForkHost.StartAsync(mutate: s => s["GraphQl:MaxExecutionDepth"] = "2");

        var response = await host.CreateClient().PostAsJsonAsync(
            "/graphql", new { query = "{ articles { items { id } } }" });
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("errors")[0].GetProperty("message").GetString()
            .Should().Contain("depth", json);
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task The_cost_limits_come_from_configuration()
    {
        await using var host = await ForkHost.StartAsync(mutate: s =>
        {
            s["GraphQl:MaxFieldCost"] = "1";
            s["GraphQl:MaxTypeCost"] = "1";
        });

        var response = await host.CreateClient().PostAsJsonAsync(
            "/graphql", new { query = "{ articles { items { id } } }" });
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var ext = doc.RootElement.GetProperty("errors")[0].GetProperty("extensions");
        ext.GetProperty("code").GetString().Should().Be("HC0047", json);
        ext.GetProperty("maxFieldCost").GetDouble().Should().Be(1.0);
    }
}
