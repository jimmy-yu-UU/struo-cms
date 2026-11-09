using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Infrastructure.DependencyInjection;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// <c>Query:MaxOffset</c> (default 100000) caps the offset of every top-level list request: REST list,
/// POST .../query and GraphQL all reach the cap through QueryValidator. An offset equal to the cap is
/// served; one above it is a 400 that names the cap and suggests narrowing with filters.
/// </summary>
[Collection("ApiIntegration")]
public class MaxOffsetTests(ApiFactory factory)
{
    private const int Cap = 100000;

    private static void AssertCapRejection(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Contain("100000").And.Contain("filter");
    }

    [Fact]
    public async Task Rest_list_serves_an_offset_equal_to_the_cap()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var resp = await c.GetAsync($"/api/items/fxCategory?offset={Cap}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rest_list_rejects_an_offset_above_the_cap_with_400()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var resp = await c.GetAsync($"/api/items/fxCategory?offset={Cap + 1}");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        AssertCapRejection(await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Query_endpoint_serves_the_cap_and_rejects_above_it()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var ok = await c.PostAsJsonAsync("/api/items/fxCategory/query", new { offset = Cap });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        var over = await c.PostAsJsonAsync("/api/items/fxCategory/query", new { offset = Cap + 1 });
        over.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        AssertCapRejection(await over.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GraphQl_list_serves_the_cap_and_rejects_above_it()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var ok = await c.PostAsJsonAsync("/graphql", new { query = $"{{ fxArticles(offset: {Cap}) {{ total }} }}" });
        using var okDoc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        okDoc.RootElement.TryGetProperty("errors", out _).Should().BeFalse(okDoc.RootElement.GetRawText());

        var over = await c.PostAsJsonAsync("/graphql", new { query = $"{{ fxArticles(offset: {Cap + 1}) {{ total }} }}" });
        using var overDoc = JsonDocument.Parse(await over.Content.ReadAsStringAsync());
        var err = overDoc.RootElement.GetProperty("errors")[0];
        err.GetProperty("message").GetString().Should().Contain("100000").And.Contain("filter");
        err.GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
    }

    [Fact]
    public async Task Query_endpoint_nested_deep_offset_serves_the_cap_and_rejects_above_it()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var ok = await c.PostAsJsonAsync("/api/items/fxCategory/query",
            new { deep = new { children = new { limit = 1, offset = Cap } } });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        var over = await c.PostAsJsonAsync("/api/items/fxCategory/query",
            new { deep = new { children = new { limit = 1, offset = Cap + 1 } } });
        over.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await over.Content.ReadAsStringAsync();
        AssertCapRejection(body);
        body.Should().Contain("children");
    }

    [Fact]
    public async Task GraphQl_nested_list_offset_serves_the_cap_and_rejects_above_it()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var ok = await c.PostAsJsonAsync("/graphql", new { query = $"{{ fxArticles {{ items {{ tags(offset: {Cap}) {{ id }} }} }} }}" });
        using var okDoc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        okDoc.RootElement.TryGetProperty("errors", out _).Should().BeFalse(okDoc.RootElement.GetRawText());

        var over = await c.PostAsJsonAsync("/graphql", new { query = $"{{ fxArticles {{ items {{ tags(offset: {Cap + 1}) {{ id }} }} }} }}" });
        using var overDoc = JsonDocument.Parse(await over.Content.ReadAsStringAsync());
        var err = overDoc.RootElement.GetProperty("errors")[0];
        err.GetProperty("message").GetString().Should().Contain("100000").And.Contain("tags");
        err.GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
    }

    [Fact]
    public void Configured_cap_binds_from_Query_MaxOffset()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Query:MaxOffset"] = "10",
        }).Build();
        using var sp = new ServiceCollection().AddSingleton<IConfiguration>(config).AddStruoData().BuildServiceProvider();

        sp.GetRequiredService<IOptions<StruoQueryOptions>>().Value.MaxOffset.Should().Be(10);
    }

    [Fact]
    public void Configured_cap_of_zero_fails_options_validation()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Query:MaxOffset"] = "0",
        }).Build();
        using var sp = new ServiceCollection().AddSingleton<IConfiguration>(config).AddStruoData().BuildServiceProvider();

        var act = () => sp.GetRequiredService<IOptions<StruoQueryOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxOffset*");
    }
}
