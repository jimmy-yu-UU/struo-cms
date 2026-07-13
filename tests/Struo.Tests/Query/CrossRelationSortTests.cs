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

    private async Task<string> Post(System.Net.Http.HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    [Fact]
    public async Task Sort_descending_by_category_name()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var catA = await Post(c, "category", new { name = "AAA_sort" });
        var catZ = await Post(c, "category", new { name = "ZZZ_sort" });
        var artA = await Post(c, "article", new { status = "draft", categoryId = catA, translations = new { en = new { title = "sortA" } } });
        var artZ = await Post(c, "article", new { status = "draft", categoryId = catZ, translations = new { en = new { title = "sortZ" } } });

        // sort=-category.name should put the ZZZ-category article before the AAA-category one.
        // Scoped to just these two articles (id _in) so the assertion doesn't depend on the
        // default unbounded page-1 window containing both rows out of the whole shared-DB
        // collection's accumulated data (other test classes keep adding articles/categories).
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_in"] = new[] { artA, artZ } } },
            sort = new[] { "-category.name" }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        var order = data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        order.IndexOf(artZ).Should().BeLessThan(order.IndexOf(artA));
    }

    [Fact]
    public async Task Sort_across_to_many_returns_400()
    {
        var c = _factory.CreateClient();
        // children is a O2M relation on category — sorting across to-many must be rejected.
        var envelope = JsonSerializer.SerializeToElement(new { sort = new[] { "-children.name" } });
        (await c.PostAsJsonAsync("/api/items/category/query", envelope)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }
}
