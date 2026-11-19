using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// fxArticle.createdAt is a plain datetime column and mediaFolder.createdAt a zone-aware one on PostgreSQL
// and SQL Server. Facets over the two must answer in either order inside one process; the order is the
// part that mattered, so each test runs the pair one way round. SQLite keeps no zone, so there is
// nothing to tell the two columns apart there.
[Collection("ApiIntegration")]
public class FacetSameNamedColumnTests(ApiFactory factory)
{
    private static async Task<List<DateTimeOffset>> FacetValues(HttpClient client, string url)
    {
        var r = await client.GetAsync(url);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var meta = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("meta");
        return meta.GetProperty("facets").GetProperty("createdAt").EnumerateArray()
            .Select(b => b.GetProperty("value").GetDateTimeOffset()).Order().ToList();
    }

    private static async Task<List<DateTimeOffset>> ListedCreatedAt(HttpClient client, string url)
    {
        var r = await client.GetAsync(url);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("data").EnumerateArray()
            .Select(i => i.GetProperty("createdAt").GetDateTimeOffset()).Distinct().Order().ToList();
    }

    private async Task<(HttpClient Client, string ArticleQuery, string FolderQuery)> SeedAsync()
    {
        var (client, cat, _) = await FacetSeed.SeedAsync(factory);
        var prefix = "same-" + Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 2; i++)
            (await client.PostAsJsonAsync("/api/items/mediaFolder", new { name = prefix + i })).StatusCode.Should().Be(HttpStatusCode.Created);
        return (client, $"/api/items/fxArticle?filter[categoryId][_eq]={cat}&limit=50",
            $"/api/items/mediaFolder?filter[name][_contains]={prefix}&limit=50");
    }

    [ZoneAwareOnlyFact("a plain datetime column and a zone-aware one named alike")]
    public async Task Plain_column_facet_then_zone_aware_column_facet_both_answer()
    {
        var (client, articles, folders) = await SeedAsync();
        (await FacetValues(client, articles + "&facets=createdAt")).Should().Equal(await ListedCreatedAt(client, articles));
        (await FacetValues(client, folders + "&facets=createdAt")).Should().Equal(await ListedCreatedAt(client, folders));
    }

    [ZoneAwareOnlyFact("a plain datetime column and a zone-aware one named alike")]
    public async Task Zone_aware_column_facet_then_plain_column_facet_both_answer()
    {
        var (client, articles, folders) = await SeedAsync();
        (await FacetValues(client, folders + "&facets=createdAt")).Should().Equal(await ListedCreatedAt(client, folders));
        (await FacetValues(client, articles + "&facets=createdAt")).Should().Equal(await ListedCreatedAt(client, articles));
    }
}
