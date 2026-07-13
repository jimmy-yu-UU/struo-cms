using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class NestedListArgsValidationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task<HttpResponseMessage> Query(string col, object envelope)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        // seed one row so validation runs (ExpandDeepAsync validates before the entities.Count==0 guard,
        // but a row makes the whole path realistic).
        await c.PostAsJsonAsync("/api/items/category", new { name = "ValSeed" });
        return await c.PostAsJsonAsync($"/api/items/{col}/query",
            JsonSerializer.SerializeToElement(envelope));
    }

    [Fact]
    public async Task Args_on_m2o_relation_return_400()
    {
        // article.category is M2O (single object) — nested list args are illegal.
        var resp = await Query("article", new { deep = new { category = new { limit = 5 } } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_nested_filter_field_returns_400()
    {
        var resp = await Query("category",
            new { deep = new { articles = new { filter = new { ghostfield = new Dictionary<string, object> { ["_eq"] = "x" } } } } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Relation_path_nested_sort_returns_400()
    {
        var resp = await Query("category",
            new { deep = new { articles = new { sort = new[] { "category.name" } } } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Negative_nested_limit_returns_400()
    {
        var resp = await Query("category",
            new { deep = new { articles = new { limit = -1 } } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Valid_nested_own_field_args_return_200()
    {
        var resp = await Query("category",
            new { deep = new { articles = new { filter = new { status = new Dictionary<string, object> { ["_eq"] = "published" } }, sort = new[] { "status" }, limit = 3, offset = 0 } } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
