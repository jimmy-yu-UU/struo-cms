using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Struo.Tests.Support;

/// <summary>
/// Shared seed for the facets/aggregate REST and GraphQL e2e suites: one category, one tag, and
/// three articles (two published — one tagged, one not — and one draft tagged), each with a
/// distinct <c>publishedAt</c> day so aggregate/facet assertions have a fixed oracle.
/// </summary>
internal static class FacetSeed
{
    private static async Task<JsonDocument> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync());

    public static async Task<(HttpClient Client, string CategoryId, string TagId)> SeedAsync(ApiFactory factory)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var cat = await client.PostAsJsonAsync("/api/items/category", new { name = "u4-" + Guid.NewGuid().ToString("N")[..8] });
        cat.StatusCode.Should().Be(HttpStatusCode.Created);
        var categoryId = (await Json(cat)).RootElement.GetProperty("data").GetProperty("id").GetString()!;
        var tag = await client.PostAsJsonAsync("/api/items/tag", new { name = "u4tag-" + Guid.NewGuid().ToString("N")[..8] });
        var tagId = (await Json(tag)).RootElement.GetProperty("data").GetProperty("id").GetString()!;
        foreach (var (status, withTag, title) in new[] { ("published", true, "Alpha"), ("published", false, "Beta"), ("draft", true, "Gamma") })
        {
            var body = new Dictionary<string, object?>
            {
                ["status"] = status, ["categoryId"] = categoryId, ["publishedAt"] = "2026-01-0" + (withTag ? "2" : "1") + "T00:00:00Z",
                ["translations"] = new { en = new { title, body = "x" } },
            };
            if (withTag) body["tags"] = new[] { tagId };
            var r = await client.PostAsJsonAsync("/api/items/article", body);
            r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        }
        return (client, categoryId, tagId);
    }
}
