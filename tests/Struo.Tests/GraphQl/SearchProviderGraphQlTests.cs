// tests/Struo.Tests/GraphQl/SearchProviderGraphQlTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Search;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

[Collection("ApiIntegration")]
public class SearchProviderGraphQlTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private HttpClient HostWith(ISearchProvider provider) =>
        _factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(provider))).CreateClient();

    private static async Task<JsonElement> Post(HttpClient client, string query)
    {
        var r = await client.PostAsJsonAsync("/graphql", new { query });
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Candidates_drive_total_on_graphql()
    {
        var (seedClient, cat, _) = await FacetSeed.SeedAsync(_factory);
        var list = await Post(seedClient, $$"""{ articles(filter: { categoryId: { eq: "{{cat}}" } }, sort: ["publishedAt"]) { items { id } total } }""");
        var anyId = list.GetProperty("data").GetProperty("articles").GetProperty("items")[0].GetProperty("id").GetString()!;
        var client = HostWith(new ScriptedSearchProvider(_ => SearchOutcome.Candidates([anyId])));
        var root = await Post(client, $$"""{ articles(filter: { categoryId: { eq: "{{cat}}" } }, search: "zzz") { total items { id } } }""");
        root.TryGetProperty("errors", out _).Should().BeFalse(root.GetRawText());
        root.GetProperty("data").GetProperty("articles").GetProperty("total").GetInt32().Should().Be(1);
        root.GetProperty("data").GetProperty("articles").GetProperty("items")[0].GetProperty("id").GetString().Should().Be(anyId);
    }

    [Fact]
    public async Task Search_unavailable_is_SEARCH_UNAVAILABLE_with_fixed_message_on_graphql()
    {
        var (_, cat, _) = await FacetSeed.SeedAsync(_factory);
        var client = HostWith(new ScriptedSearchProvider(_ => throw new SearchUnavailableException("meilisearch at 10.0.0.5:7700 refused")));
        var root = await Post(client, $$"""{ articles(filter: { categoryId: { eq: "{{cat}}" } }, search: "zzz") { total } }""");
        root.GetRawText().Should().NotContain("10.0.0.5");
        var error = root.GetProperty("errors")[0];
        error.GetProperty("message").GetString().Should().Be("Search is temporarily unavailable.");
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("SEARCH_UNAVAILABLE");
    }
}
