// tests/Struo.Tests/Api/SearchProviderApiTests.cs
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Search;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class SearchProviderApiTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static async Task<JsonDocument> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync());

    // Derived host sharing the base factory's SQLite database; last registration wins for ISearchProvider.
    private HttpClient HostWith(ISearchProvider provider) =>
        _factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(provider))).CreateClient();

    // FacetSeed's only draft article is "Gamma", so a status filter identifies it without depending on
    // the translations projection shape.
    private async Task<(string CategoryId, string GammaId)> SeedAsync()
    {
        var (client, cat, _) = await FacetSeed.SeedAsync(_factory);
        var drafts = await Json(await client.GetAsync($"/api/items/article?filter[categoryId][_eq]={cat}&filter[status][_eq]=draft"));
        var gamma = drafts.RootElement.GetProperty("data").EnumerateArray().Single().GetProperty("id").GetString()!;
        return (cat, gamma);
    }

    [Fact]
    public void Default_host_resolves_NullSearchProvider()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISearchProvider>().Should().BeOfType<NullSearchProvider>();
    }

    [Fact]
    public async Task Candidates_drive_data_total_and_facets_on_rest()
    {
        var (cat, gammaId) = await SeedAsync();
        var provider = new ScriptedSearchProvider(_ => SearchOutcome.Candidates([gammaId]));
        var r = await HostWith(provider).GetAsync($"/api/items/article?filter[categoryId][_eq]={cat}&search=zzz&facets=status");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var root = (await Json(r)).RootElement;
        root.GetProperty("meta").GetProperty("total").GetInt64().Should().Be(1);
        root.GetProperty("data")[0].GetProperty("id").GetString().Should().Be(gammaId);
        var status = root.GetProperty("meta").GetProperty("facets").GetProperty("status").EnumerateArray().ToList();
        status.Should().ContainSingle().Which.GetProperty("value").GetString().Should().Be("draft");
        provider.Requests.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Collection = "article", Term = "zzz" });
    }

    [Fact]
    public async Task Not_handled_keeps_the_like_search_on_rest()
    {
        var (cat, _) = await SeedAsync();
        var client = HostWith(new ScriptedSearchProvider(_ => SearchOutcome.NotHandled));
        var r = (await Json(await client.GetAsync($"/api/items/article?filter[categoryId][_eq]={cat}&search=Alpha"))).RootElement;
        r.GetProperty("meta").GetProperty("total").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task Search_unavailable_is_503_with_the_fixed_message_on_rest()
    {
        var (cat, _) = await SeedAsync();
        var client = HostWith(new ScriptedSearchProvider(_ => throw new SearchUnavailableException("meilisearch at 10.0.0.5:7700 refused")));
        var r = await client.GetAsync($"/api/items/article?filter[categoryId][_eq]={cat}&search=zzz");
        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await r.Content.ReadAsStringAsync();
        body.Should().NotContain("10.0.0.5");
        var error = JsonDocument.Parse(body).RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("SEARCH_UNAVAILABLE");
        error.GetProperty("message").GetString().Should().Be("Search is temporarily unavailable.");
    }
}
