using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class DeepNestingExpansionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> Post(HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    [Fact]
    public async Task Depth2_m2o_chain_article_category_parent_resolves()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var grandparent = await Post(c, "category", new { name = "GP-8c3a" });
        var parent = await Post(c, "category", new { name = "P-8c3a", parentId = grandparent });
        var child = await Post(c, "category", new { name = "C-8c3a", parentId = parent });
        var articleId = await Post(c, "article",
            new { status = "draft", categoryId = child, translations = new { en = new { title = "A-8c3a" } } });

        // article -> category (child) -> parent (P) -> parent (GP): depth-3 M2O chain.
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = articleId } },
            deep = new { category = new { deep = new { parent = new { deep = new { parent = new { } } } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        var cat = row.GetProperty("category");
        cat.GetProperty("name").GetString().Should().Be("C-8c3a");
        var p = cat.GetProperty("parent");
        p.GetProperty("name").GetString().Should().Be("P-8c3a");
        p.GetProperty("parent").GetProperty("name").GetString().Should().Be("GP-8c3a"); // depth-3 resolved
    }

    [Fact]
    public async Task Depth2_o2m_then_m2o_category_articles_category_resolves()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "O2MParent-8c3a" });
        await Post(c, "article",
            new { status = "draft", categoryId = cat, translations = new { en = new { title = "Kid-8c3a" } } });

        // category -> articles (O2M) -> category (M2O back to the same category): mixed-kind depth-2.
        // NOTE: nested `deep` is only supported via the POST /query JSON envelope — the GET query-string
        // `deep=` form is deliberately kept flat/depth-1 (ParseDeepQueryString unchanged),
        // so this scenario is driven through the envelope rather than the GET route the original brief
        // sketch used.
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = cat } },
            deep = new { articles = new { deep = new { category = new { } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        var articles = row.GetProperty("articles");
        articles.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        articles[0].GetProperty("category").GetProperty("name").GetString().Should().Be("O2MParent-8c3a");
    }

    [Fact]
    public async Task Depth2_self_ref_o2m_children_resolves_cjk()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var root = await Post(c, "category", new { name = "根-8c3a" });   // CJK: 根 = U+6839
        await Post(c, "category", new { name = "子-8c3a", parentId = root }); // 子 = U+5B50

        // category -> children (O2M self-ref) -> children (empty, but the level resolves).
        // NOTE: see comment in the previous test — nested `deep` goes through the POST /query envelope,
        // not the flat GET query-string form.
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = root } },
            deep = new { children = new { deep = new { children = new { } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        var children = row.GetProperty("children");
        var childNames = children.EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToList();
        childNames.Should().Contain("子-8c3a"); // CJK round-trips; nested `children` key present on each
        children[0].TryGetProperty("children", out var grand).Should().BeTrue();
        grand.ValueKind.Should().Be(JsonValueKind.Array); // depth-2 self-ref level materialised
    }

    [Fact]
    public async Task Depth2_o2m_then_m2m_category_articles_tags_resolves()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "M2MParent-8c3a" });
        var tag = await Post(c, "tag", new { name = "標籤-8c3a" }); // CJK: 標籤 = U+6A19 U+7C64
        await Post(c, "article", new
        {
            status = "draft",
            categoryId = cat,
            tags = new[] { tag },
            translations = new { en = new { title = "Tagged-8c3a" } }
        });

        // category -> articles (O2M) -> tags (M2M): nested recursion running THROUGH an M2M level
        // at depth-2 (the `expanded.Add((targets[tid], d)); recurse` branch in the M2M relation-
        // expansion path), not just at the top level like CrossRelationFilterTests' M2M filter test.
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = cat } },
            deep = new { articles = new { deep = new { tags = new { } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        var articles = row.GetProperty("articles");
        articles.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        var tags = articles[0].GetProperty("tags");
        tags.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        tags[0].GetProperty("name").GetString().Should().Be("標籤-8c3a"); // CJK round-trips at depth-2 through M2M
    }
}
