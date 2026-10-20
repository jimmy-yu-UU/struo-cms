using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// With exactly one parent expanded through a to-many (O2M) relation and a nested limit, the
// nested sort/offset/limit run in SQL and MaxExpandRows does not apply; every other shape keeps
// the capped in-memory path.
[Collection("ApiIntegration")]
public class SingleParentWindowPushdownTests(ApiFactory factory)
{
    private const int SmallCap = 3;

    private readonly ApiFactory _factory = factory;

    private static readonly string[] SortByName = ["name"];

    private static Dictionary<string, object> Op(string op, object value) => new() { [op] = value };

    private static async Task<(HttpStatusCode Status, string Body)> Query(HttpClient c, string col, object envelope)
    {
        var resp = await c.PostAsJsonAsync($"/api/items/{col}/query", JsonSerializer.SerializeToElement(envelope));
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private async Task<Guid> SeedParent(string label, params string[] childNames)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var parent = new Category { Id = Guid.NewGuid(), Name = $"{label}-parent" };
        await db.Insertable(parent).ExecuteCommandAsync();
        var children = childNames
            .Select(n => new Category { Id = Guid.NewGuid(), Name = n, ParentId = parent.Id })
            .ToList();
        await db.Insertable(children).ExecuteCommandAsync();
        return parent.Id;
    }

    private async Task DeleteFamilies(params Guid[] parentIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        foreach (var id in parentIds)
        {
            await db.Deleteable<Category>().Where(x => x.ParentId == id).ExecuteCommandAsync();
            await db.Deleteable<Category>().Where(x => x.Id == id).ExecuteCommandAsync();
        }
    }

    private async Task<T> WithSmallCap<T>(Func<Task<T>> body)
    {
        var options = _factory.Services.GetRequiredService<StruoQueryOptions>();
        var original = options.MaxExpandRows;
        options.MaxExpandRows = SmallCap;
        try { return await body(); }
        finally { options.MaxExpandRows = original; }
    }

    private static IReadOnlyList<JsonElement> Children(string body, int parentIndex = 0)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("data")[parentIndex].GetProperty("children")
            .EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static object Envelope(object parentFilter, object nested) =>
        new { filter = parentFilter, deep = new { children = nested } };

    private static object ParentById(Guid id) => new { id = Op("_eq", id) };

    [Fact]
    public async Task One_parent_with_a_limit_returns_the_sorted_window_beyond_the_cap()
    {
        var parentId = await SeedParent("win", "e", "b", "d", "a", "c", "g", "f");
        try
        {
            var c = await _factory.CreateAuthenticatedClientAsync();
            var (status, body) = await WithSmallCap(() => Query(c, "category",
                Envelope(ParentById(parentId), new { sort = SortByName, offset = 2, limit = 3 })));
            status.Should().Be(HttpStatusCode.OK);
            Children(body).Select(e => e.GetProperty("name").GetString()).Should().Equal("c", "d", "e");
        }
        finally { await DeleteFamilies(parentId); }
    }

    [Fact]
    public async Task One_parent_pages_over_tied_sort_keys_are_disjoint_and_complete()
    {
        var parentId = await SeedParent("ties", "t", "t", "t", "u", "u", "u", "v", "v");
        try
        {
            var c = await _factory.CreateAuthenticatedClientAsync();
            var ids = new List<string>();
            foreach (var offset in new[] { 0, 3, 6 })
            {
                var (status, body) = await WithSmallCap(() => Query(c, "category",
                    Envelope(ParentById(parentId), new { sort = SortByName, offset, limit = 3 })));
                status.Should().Be(HttpStatusCode.OK);
                ids.AddRange(Children(body).Select(e => e.GetProperty("id").GetString()!));
            }
            ids.Should().HaveCount(8).And.OnlyHaveUniqueItems();
        }
        finally { await DeleteFamilies(parentId); }
    }

    [Fact]
    public async Task One_parent_with_a_limit_applies_the_nested_filter_in_the_window()
    {
        var parentId = await SeedParent("filt", "dup", "x", "dup", "dup", "y", "dup", "dup");
        try
        {
            var c = await _factory.CreateAuthenticatedClientAsync();
            var (status, body) = await WithSmallCap(() => Query(c, "category",
                Envelope(ParentById(parentId), new { filter = new { name = Op("_eq", "dup") }, limit = 2 })));
            status.Should().Be(HttpStatusCode.OK);
            Children(body).Select(e => e.GetProperty("name").GetString()).Should().Equal("dup", "dup");
        }
        finally { await DeleteFamilies(parentId); }
    }

    [Fact]
    public async Task One_parent_without_a_limit_keeps_the_row_cap()
    {
        var parentId = await SeedParent("nolimit", "a", "b", "c", "d");
        try
        {
            var c = await _factory.CreateAuthenticatedClientAsync();
            var (status, body) = await WithSmallCap(() => Query(c, "category",
                Envelope(ParentById(parentId), new { sort = SortByName, offset = 1 })));
            status.Should().Be(HttpStatusCode.BadRequest);
            body.Should().Contain("children");
        }
        finally { await DeleteFamilies(parentId); }
    }

    [Fact]
    public async Task Several_parents_with_a_limit_keep_the_row_cap()
    {
        var first = await SeedParent("multi-a", "a", "b");
        var second = await SeedParent("multi-b", "c", "d");
        try
        {
            var c = await _factory.CreateAuthenticatedClientAsync();
            var (status, body) = await WithSmallCap(() => Query(c, "category",
                Envelope(new { id = Op("_in", new[] { first, second }) }, new { sort = SortByName, limit = 1 })));
            status.Should().Be(HttpStatusCode.BadRequest);
            body.Should().Contain("children");
        }
        finally { await DeleteFamilies(first, second); }
    }

    [Fact]
    public async Task Many_to_many_with_a_limit_keeps_the_junction_row_cap()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var tagIds = new List<string>();
        foreach (var n in new[] { "pw-1", "pw-2", "pw-3", "pw-4" })
        {
            var r = await c.PostAsJsonAsync("/api/items/tag", new { name = n });
            using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            tagIds.Add(d.RootElement.GetProperty("data").GetProperty("id").GetString()!);
        }
        var created = await c.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "PushdownM2M" } }, tags = tagIds });
        using var cd = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var articleId = Guid.Parse(cd.RootElement.GetProperty("data").GetProperty("id").GetString()!);

        var (status, body) = await WithSmallCap(() => Query(c, "article",
            new { filter = ParentById(articleId), deep = new { tags = new { limit = 2 } } }));
        status.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("'tags'").And.Contain("junction rows");
    }
}
