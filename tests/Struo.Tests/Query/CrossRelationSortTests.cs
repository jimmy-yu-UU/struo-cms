using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class CrossRelationSortTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<long> Post(System.Net.Http.HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

    [Fact]
    public async Task Sort_descending_by_category_name()
    {
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "S1" });
        var catA = await Post(c, "category", new { name = "AAA_sort" });
        var catZ = await Post(c, "category", new { name = "ZZZ_sort" });
        var artA = await Post(c, "article", new { status = "draft", authorId = author, categoryId = catA, translations = new { en = new { title = "sortA" } } });
        var artZ = await Post(c, "article", new { status = "draft", authorId = author, categoryId = catZ, translations = new { en = new { title = "sortZ" } } });

        // sort=-category.name should put the ZZZ-category article before the AAA-category one.
        var envelope = JsonSerializer.SerializeToElement(new { sort = new[] { "-category.name" } });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        var order = data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        order.IndexOf(artZ).Should().BeLessThan(order.IndexOf(artA));
    }

    [Fact]
    public async Task Sort_across_to_many_returns_400()
    {
        var c = _factory.CreateClient();
        var envelope = JsonSerializer.SerializeToElement(new { sort = new[] { "-tags.name" } });
        (await c.PostAsJsonAsync("/api/items/article/query", envelope)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
