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
        var hit = await Post(c, "article", new { title = "HIT", status = "draft", authorId = author, categoryId = news });
        await Post(c, "article", new { title = "MISS", status = "draft", authorId = author, categoryId = other });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.name"] = Eq("FilterNews") }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        ids.Should().Contain(hit);
        foreach (var r in data.EnumerateArray())
            r.GetProperty("title").GetString().Should().Be("HIT");
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
        var hit = await Post(c, "article", new { title = "DEEPHIT", status = "draft", authorId = author, categoryId = child });

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
        var byCat = await Post(c, "article", new { title = "ZZZ", status = "draft", authorId = author, categoryId = cat });
        var byTitle = await Post(c, "article", new { title = "OrTitleUnique", status = "draft", authorId = author });

        // _or over a relation-path condition and a scalar condition
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object>
            {
                ["_or"] = new object[]
                {
                    new Dictionary<string, object> { ["category.name"] = Eq("OrCat") },
                    new Dictionary<string, object> { ["title"] = Eq("OrTitleUnique") }
                }
            }
        });
        var ids = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync())
            .GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        ids.Should().Contain(byCat).And.Contain(byTitle);
    }

    [Fact]
    public async Task Filter_m2m_tags_name_exists()
    {
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A4" });
        var tag = await Post(c, "tag", new { name = "CSharpTag" });
        var hit = await Post(c, "article", new { title = "TAGGED", status = "draft", authorId = author, tags = new[] { tag } });
        await Post(c, "article", new { title = "UNTAGGED", status = "draft", authorId = author });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["tags.name"] = Eq("CSharpTag") }
        });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
        var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        ids.Should().Contain(hit);
        foreach (var r in data.EnumerateArray())
            r.GetProperty("title").GetString().Should().Be("TAGGED");
    }

    [Fact]
    public async Task Filter_o2m_category_by_article_title()
    {
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A5" });
        var cat = await Post(c, "category", new { name = "O2MFilterCat" });
        await Post(c, "article", new { title = "UniqueChildTitle", status = "draft", authorId = author, categoryId = cat });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["articles.title"] = Eq("UniqueChildTitle") }
        });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/category/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
        data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).Should().Contain(cat);
    }
}
