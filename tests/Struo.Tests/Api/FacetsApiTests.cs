using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class FacetsApiTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static async Task<JsonDocument> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync());

    private async Task<(HttpClient Client, string CategoryId, string TagId)> SeedAsync()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
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

    [Fact]
    public async Task Facets_and_aggregate_appear_in_meta_only_when_requested()
    {
        var (client, cat, _) = await SeedAsync();
        var plain = await Json(await client.GetAsync($"/api/items/article?filter[categoryId][_eq]={cat}"));
        plain.RootElement.GetProperty("meta").TryGetProperty("facets", out _).Should().BeFalse();
        plain.RootElement.GetProperty("meta").TryGetProperty("aggregate", out _).Should().BeFalse();

        var r = await client.GetAsync($"/api/items/article?filter[categoryId][_eq]={cat}&facets=status,tags,category.name&aggregate[count]=publishedAt&aggregate[max]=publishedAt");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var meta = (await Json(r)).RootElement.GetProperty("meta");
        var status = meta.GetProperty("facets").GetProperty("status").EnumerateArray()
            .Select(b => (b.GetProperty("value").GetString(), b.GetProperty("count").GetInt64())).ToList();
        status.Should().Equal(("published", 2), ("draft", 1));
        meta.GetProperty("facets").GetProperty("tags").EnumerateArray().Should().HaveCount(1);
        meta.GetProperty("facets").GetProperty("tags")[0].GetProperty("count").GetInt64().Should().Be(2);
        meta.GetProperty("facets").GetProperty("category.name")[0].GetProperty("count").GetInt64().Should().Be(3);
        meta.GetProperty("aggregate").GetProperty("count").GetProperty("publishedAt").GetInt64().Should().Be(3);
        meta.GetProperty("aggregate").GetProperty("max").GetProperty("publishedAt").GetString().Should().StartWith("2026-01-02");
    }

    [Fact]
    public async Task Facet_counts_are_disjunctive_and_match_the_filtered_total_oracle()
    {
        var (client, cat, _) = await SeedAsync();
        var r = await Json(await client.GetAsync($"/api/items/article?filter[categoryId][_eq]={cat}&filter[status][_eq]=draft&facets=status&limit=1"));
        var buckets = r.RootElement.GetProperty("meta").GetProperty("facets").GetProperty("status").EnumerateArray().ToList();
        buckets.Should().HaveCount(2);
        foreach (var b in buckets)
        {
            var v = b.GetProperty("value").GetString();
            var oracle = await Json(await client.GetAsync($"/api/items/article?filter[categoryId][_eq]={cat}&filter[status][_eq]={v}&limit=1"));
            oracle.RootElement.GetProperty("meta").GetProperty("total").GetInt64().Should().Be(b.GetProperty("count").GetInt64());
        }
    }

    [Fact]
    public async Task Pagination_does_not_change_facets_or_aggregate()
    {
        var (client, cat, _) = await SeedAsync();
        var a = await Json(await client.GetAsync($"/api/items/article?filter[categoryId][_eq]={cat}&facets=status&aggregate[count]=status"));
        var b = await Json(await client.GetAsync($"/api/items/article?filter[categoryId][_eq]={cat}&facets=status&aggregate[count]=status&limit=1&offset=2"));
        b.RootElement.GetProperty("data").GetArrayLength().Should().Be(1);
        b.RootElement.GetProperty("meta").GetProperty("facets").GetRawText().Should().Be(a.RootElement.GetProperty("meta").GetProperty("facets").GetRawText());
        b.RootElement.GetProperty("meta").GetProperty("aggregate").GetRawText().Should().Be(a.RootElement.GetProperty("meta").GetProperty("aggregate").GetRawText());
    }

    [Fact]
    public async Task Envelope_query_is_equivalent_to_the_query_string()
    {
        var (client, cat, _) = await SeedAsync();
        var qs = await Json(await client.GetAsync($"/api/items/article?filter[categoryId][_eq]={cat}&facets=status&aggregate[count]=publishedAt"));
        var env = await Json(await client.PostAsJsonAsync("/api/items/article/query", new
        {
            filter = new { categoryId = new { _eq = cat } }, facets = new[] { "status" }, aggregate = new { count = new[] { "publishedAt" } },
        }));
        env.RootElement.GetProperty("meta").GetProperty("facets").GetRawText().Should().Be(qs.RootElement.GetProperty("meta").GetProperty("facets").GetRawText());
        env.RootElement.GetProperty("meta").GetProperty("aggregate").GetRawText().Should().Be(qs.RootElement.GetProperty("meta").GetProperty("aggregate").GetRawText());
    }

    [Fact]
    public async Task Translatable_leaf_facet_follows_the_locale()
    {
        var (client, cat, _) = await SeedAsync();
        var en = await Json(await client.GetAsync($"/api/items/category?filter[id][_eq]={cat}&facets=articles.title"));
        en.RootElement.GetProperty("meta").GetProperty("facets").GetProperty("articles.title").EnumerateArray()
            .Select(b => b.GetProperty("value").GetString()).Should().BeEquivalentTo("Alpha", "Beta", "Gamma");
        var zh = await Json(await client.GetAsync($"/api/items/category?filter[id][_eq]={cat}&facets=articles.title&locale=zh-TW"));
        var zhBuckets = zh.RootElement.GetProperty("meta").GetProperty("facets").GetProperty("articles.title").EnumerateArray().ToList();
        zhBuckets.Should().HaveCount(1);
        zhBuckets[0].GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        zhBuckets[0].GetProperty("count").GetInt64().Should().Be(3);
    }

    [Theory]
    [InlineData("facets=nope", "Unknown field 'nope' on collection 'article'.")]
    [InlineData("facets=regions", "cannot be used as a facet")]
    [InlineData("facets=category.parent.name", "exactly one relation hop")]
    [InlineData("facets=tags._junction.note", "cannot contain quantifiers or '_junction'")]
    [InlineData("aggregate[sum]=status", "Aggregate 'sum' is not supported on field 'status'")]
    [InlineData("aggregate[median]=status", "Unknown aggregate op 'median'")]
    public async Task Invalid_facet_or_aggregate_requests_are_400_with_the_validator_message(string query, string fragment)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var r = await client.GetAsync("/api/items/article?" + query);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain(fragment);
    }
}
