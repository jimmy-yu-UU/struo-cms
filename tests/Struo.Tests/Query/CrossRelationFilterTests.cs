using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class CrossRelationFilterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static object Eq(object v) => new Dictionary<string, object> { ["_eq"] = v };

    private async Task<long> Post(System.Net.Http.HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

    [Fact]
    public async Task Filter_to_one_category_name()
    {
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A1" });
        var news = await Post(c, "category", new { name = "FilterNews" });
        var other = await Post(c, "category", new { name = "FilterOther" });
        var hit = await Post(c, "article", new { status = "draft", authorId = author, categoryId = news, translations = new { en = new { title = "HIT" } } });
        var miss = await Post(c, "article", new { status = "draft", authorId = author, categoryId = other, translations = new { en = new { title = "MISS" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.name"] = Eq("FilterNews") }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        // title is now translatable (sidecar); assert by id rather than top-level title.
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
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A2" });
        var parent = await Post(c, "category", new { name = "ParentCat" });
        var child = await Post(c, "category", new { name = "ChildCat", parentId = parent });
        var hit = await Post(c, "article", new { status = "draft", authorId = author, categoryId = child, translations = new { en = new { title = "DEEPHIT" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.parent.name"] = Eq("ParentCat") }
        });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
        data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).Should().Contain(hit);
    }

    [Fact]
    public async Task Filter_relation_path_or_scalar_composes()
    {
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A3" });
        var cat = await Post(c, "category", new { name = "OrCat" });
        var byCat = await Post(c, "article", new { status = "draft", authorId = author, categoryId = cat, translations = new { en = new { title = "ZZZ" } } });
        // title is translatable now; the scalar OR branch filters by a non-translatable own field (id) instead.
        var byId = await Post(c, "article", new { status = "draft", authorId = author, translations = new { en = new { title = "OrIdUnique" } } });

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
            .GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        ids.Should().Contain(byCat).And.Contain(byId);
    }

    [Fact]
    public async Task Filter_m2m_tags_name_exists()
    {
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A4" });
        var tag = await Post(c, "tag", new { name = "CSharpTag" });
        var hit = await Post(c, "article", new { status = "draft", authorId = author, tags = new[] { tag }, translations = new { en = new { title = "TAGGED" } } });
        var untagged = await Post(c, "article", new { status = "draft", authorId = author, translations = new { en = new { title = "UNTAGGED" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["tags.name"] = Eq("CSharpTag") }
        });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
        var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        // title is translatable now; assert by id rather than top-level title.
        ids.Should().Contain(hit);
        ids.Should().NotContain(untagged);
    }

    [Fact]
    public async Task Filter_o2m_category_by_article_id()
    {
        // NOTE: filtering an o2m relation by the child's translatable `title` is locale-aware
        // relation querying (Task 5). Here we filter the o2m relation by the child's
        // non-translatable own-collection field (id) to exercise the same two-phase resolution.
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A5" });
        var cat = await Post(c, "category", new { name = "O2MFilterCat" });
        var childArticle = await Post(c, "article", new { status = "draft", authorId = author, categoryId = cat, translations = new { en = new { title = "UniqueChildTitle" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["articles.id"] = Eq(childArticle) }
        });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/category/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
        data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).Should().Contain(cat);
    }

    [Fact]
    public async Task Filter_empty_relation_under_or_keeps_scalar_match()
    {
        // _or: relation branch matches nothing, scalar branch matches one article.
        // The empty relation branch must contribute zero ids — not swallow the whole OR.
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A6" });
        var hit = await Post(c, "article", new { status = "draft", authorId = author, translations = new { en = new { title = "OrEmptyUnique" } } });

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
            .Select(r => r.GetProperty("id").GetInt64()).ToList();
        ids.Should().Contain(hit);
    }

    [Fact]
    public async Task Filter_empty_relation_under_and_returns_zero()
    {
        // _and: relation branch matches nothing, so the whole AND must return 0 rows
        // even though the scalar branch would match.
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A7" });
        var art = await Post(c, "article", new { status = "draft", authorId = author, translations = new { en = new { title = "AndEmptyUnique" } } });

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
