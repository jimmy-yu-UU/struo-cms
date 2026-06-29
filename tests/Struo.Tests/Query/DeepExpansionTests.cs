using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// NOTE: Deep_expands_m2o_author was deleted in Phase 5.5 (Author entity removed).
// Deep_m2o_honors_field_whitelist_via_envelope now tests category (M2O) instead of author.

[Collection("ApiIntegration")]
public class DeepExpansionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task Deep_unknown_relation_returns_400()
    {
        var c = _factory.CreateClient();
        var id = Root(await (await c.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "Y" } } })).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;
        (await c.GetAsync($"/api/items/article/{id}?deep=ghostrel")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_without_deep_has_no_relation_keys()
    {
        var c = _factory.CreateClient();
        await c.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "NoDeep" } } });

        var data = Root(await (await c.GetAsync("/api/items/article?limit=50")).Content.ReadAsStringAsync()).GetProperty("data");
        data.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var row in data.EnumerateArray())
            row.TryGetProperty("category", out _).Should().BeFalse();  // no relation keys without ?deep
    }

    [Fact]
    public async Task Deep_expands_o2m_articles_for_category()
    {
        var c = _factory.CreateClient();
        var categoryId = Root(await (await c.PostAsJsonAsync("/api/items/category",
            new { name = "News" })).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;
        await c.PostAsJsonAsync("/api/items/article",
            new { status = "draft", categoryId, translations = new { en = new { title = "Child1" } } });
        await c.PostAsJsonAsync("/api/items/article",
            new { status = "draft", categoryId, translations = new { en = new { title = "Child2" } } });

        var resp = await c.GetAsync($"/api/items/category/{categoryId}?deep=articles");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var articles = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("articles");
        articles.ValueKind.Should().Be(JsonValueKind.Array);
        articles.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Deep_m2o_honors_field_whitelist_via_envelope()
    {
        var c = _factory.CreateClient();
        var categoryId = Root(await (await c.PostAsJsonAsync("/api/items/category",
            new { name = "Whitelisted" })).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;
        var articleId = Root(await (await c.PostAsJsonAsync("/api/items/article",
            new { status = "draft", categoryId, translations = new { en = new { title = "WL" } } })).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = articleId } },
            deep = new { category = new { fields = new[] { "name" } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        var category = first.GetProperty("category");
        category.GetProperty("name").GetString().Should().Be("Whitelisted");
        category.GetProperty("id").GetString().Should().Be(categoryId); // id is always included
    }
}
