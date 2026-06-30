using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// NOTE: Filter_m2m_tags_name_exists was deleted in Phase 5.5 (Tag entity removed).

[Collection("ApiIntegration")]
public class CrossRelationFilterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static object Eq(object v) => new Dictionary<string, object> { ["_eq"] = v };

    private async Task<string> Post(System.Net.Http.HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    [Fact]
    public async Task Filter_to_one_category_name()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var news = await Post(c, "category", new { name = "FilterNews" });
        var other = await Post(c, "category", new { name = "FilterOther" });
        var hit = await Post(c, "article", new { status = "draft", categoryId = news, translations = new { en = new { title = "HIT" } } });
        var miss = await Post(c, "article", new { status = "draft", categoryId = other, translations = new { en = new { title = "MISS" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.name"] = Eq("FilterNews") }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        ids.Should().Contain(hit);
        ids.Should().NotContain(miss);
    }

    [Fact]
    public async Task Filter_empty_match_returns_zero_rows()
    {
        var c = _factory.CreateClient();
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.name"] = Eq("NoSuchCategoryXYZ") }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Filter_multi_level_category_parent_name()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var parent = await Post(c, "category", new { name = "ParentCat" });
        var child = await Post(c, "category", new { name = "ChildCat", parentId = parent });
        var hit = await Post(c, "article", new { status = "draft", categoryId = child, translations = new { en = new { title = "DEEPHIT" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.parent.name"] = Eq("ParentCat") }
        });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
        data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).Should().Contain(hit);
    }

    [Fact]
    public async Task Filter_relation_path_or_scalar_composes()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "OrCat" });
        var byCat = await Post(c, "article", new { status = "draft", categoryId = cat, translations = new { en = new { title = "ZZZ" } } });
        var byId = await Post(c, "article", new { status = "draft", translations = new { en = new { title = "OrIdUnique" } } });

        // _or over a relation-path condition and a scalar condition
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object>
            {
                ["_or"] = new object[]
                {
                    new Dictionary<string, object> { ["category.name"] = Eq("OrCat") },
                    new Dictionary<string, object> { ["id"] = Eq(byId) }
                }
            }
        });
        var ids = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync())
            .GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        ids.Should().Contain(byCat).And.Contain(byId);
    }

    [Fact]
    public async Task Filter_o2m_category_by_article_id()
    {
        // NOTE: filtering an o2m relation by the child's translatable `title` is locale-aware
        // relation querying (Task 5). Here we filter the o2m relation by the child's
        // non-translatable own-collection field (id) to exercise the same two-phase resolution.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "O2MFilterCat" });
        var childArticle = await Post(c, "article", new { status = "draft", categoryId = cat, translations = new { en = new { title = "UniqueChildTitle" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["articles.id"] = Eq(childArticle) }
        });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/category/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
        data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).Should().Contain(cat);
    }

    [Fact]
    public async Task Filter_empty_relation_under_or_keeps_scalar_match()
    {
        // _or: relation branch matches nothing, scalar branch matches one article.
        // The empty relation branch must contribute zero ids — not swallow the whole OR.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var hit = await Post(c, "article", new { status = "draft", translations = new { en = new { title = "OrEmptyUnique" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object>
            {
                ["_or"] = new object[]
                {
                    new Dictionary<string, object> { ["category.name"] = Eq("NoSuchCat_or") },
                    new Dictionary<string, object> { ["id"] = Eq(hit) }
                }
            }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var ids = Root(await resp.Content.ReadAsStringAsync())
            .GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("id").GetString()).ToList();
        ids.Should().Contain(hit);
    }

    [Fact]
    public async Task Filter_empty_relation_under_and_returns_zero()
    {
        // _and: relation branch matches nothing, so the whole AND must return 0 rows
        // even though the scalar branch would match.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var art = await Post(c, "article", new { status = "draft", translations = new { en = new { title = "AndEmptyUnique" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object>
            {
                ["_and"] = new object[]
                {
                    new Dictionary<string, object> { ["category.name"] = Eq("NoSuchCat_and") },
                    new Dictionary<string, object> { ["id"] = Eq(art) }
                }
            }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetArrayLength().Should().Be(0);
    }
}
