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

[Collection("ApiIntegration")]
public class QueryCapsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static readonly string[] FiveSorts = ["name", "-name", "createdAt", "-createdAt", "id"];
    private static readonly string[] SixSorts = ["name", "-name", "createdAt", "-createdAt", "id", "-id"];

    private static async Task<(HttpStatusCode Status, string Body)> Query(HttpClient c, string col, object envelope)
    {
        var resp = await c.PostAsJsonAsync($"/api/items/{col}/query", JsonSerializer.SerializeToElement(envelope));
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static Dictionary<string, object> Eq(string field, object value) =>
        new() { [field] = new Dictionary<string, object> { ["_eq"] = value } };

    // A filter holding `n` own-field conditions on the sample's `name` field.
    private static Dictionary<string, object> Conditions(int n) => new()
    {
        ["_and"] = Enumerable.Range(0, n).Select(i => (object)Eq("name", $"cap-{i}")).ToList(),
    };

    // Builds a category self-relation tree (parent/children) holding exactly `nodes` relation nodes.
    private static Dictionary<string, object> Tree(int nodes)
    {
        var remaining = nodes;
        return Fill(ref remaining, depthLeft: 6);
    }

    private static Dictionary<string, object> Fill(ref int remaining, int depthLeft)
    {
        var map = new Dictionary<string, object>();
        foreach (var name in new[] { "parent", "children" })
        {
            if (remaining == 0) break;
            remaining--;
            map[name] = depthLeft > 1
                ? new Dictionary<string, object> { ["deep"] = Fill(ref remaining, depthLeft - 1) }
                : new Dictionary<string, object>();
        }
        return map;
    }

    private async Task<HttpClient> SeededClient()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        await c.PostAsJsonAsync("/api/items/category", new { name = "CapsSeed" });
        return c;
    }

    [Fact]
    public async Task Five_sort_fields_are_accepted()
    {
        var c = await SeededClient();
        (await Query(c, "category", new { sort = FiveSorts })).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Six_sort_fields_return_400()
    {
        var c = await SeededClient();
        var (status, body) = await Query(c, "category", new { sort = SixSorts });
        status.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("sort");
    }

    [Fact]
    public async Task Six_nested_sort_fields_return_400()
    {
        var c = await SeededClient();
        var (status, _) = await Query(c, "category",
            new { deep = new { children = new { sort = SixSorts } } });
        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Twenty_relation_nodes_are_accepted()
    {
        var c = await SeededClient();
        (await Query(c, "category", new { deep = Tree(20) })).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Twenty_one_relation_nodes_return_400()
    {
        var c = await SeededClient();
        var (status, body) = await Query(c, "category", new { deep = Tree(21) });
        status.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("20");
    }

    [Fact]
    public async Task Filter_conditions_share_one_budget_across_root_and_nested_filters()
    {
        var c = await SeededClient();
        var (status, body) = await Query(c, "category", new
        {
            filter = Conditions(30),
            deep = new { children = new { filter = Conditions(30) } },
        });
        status.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("filter conditions");
    }

    [Fact]
    public async Task Root_and_nested_conditions_within_the_budget_are_accepted()
    {
        var c = await SeededClient();
        (await Query(c, "category", new
        {
            filter = Conditions(25),
            deep = new { children = new { filter = Conditions(25) } },
        })).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Large_offset_is_still_accepted()
    {
        var c = await SeededClient();
        (await Query(c, "category", new { offset = 100000 })).Status.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> SeedParentWithChildren(int count, string label)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var parent = new Category { Id = Guid.NewGuid(), Name = $"{label}-parent" };
        await db.Insertable(parent).ExecuteCommandAsync();
        var children = Enumerable.Range(0, count)
            .Select(i => new Category { Id = Guid.NewGuid(), Name = $"{label}-child-{i}", ParentId = parent.Id })
            .ToList();
        await db.Insertable(children).ExecuteCommandAsync();
        return parent.Id;
    }

    private async Task DeleteFamily(Guid parentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Deleteable<Category>().Where(x => x.ParentId == parentId).ExecuteCommandAsync();
        await db.Deleteable<Category>().Where(x => x.Id == parentId).ExecuteCommandAsync();
    }

    [Fact]
    public async Task One_to_many_expansion_over_the_row_cap_returns_400_naming_the_relation()
    {
        var max = _factory.Services.GetRequiredService<StruoQueryOptions>().MaxExpandRows;
        var parentId = await SeedParentWithChildren(max + 1, "caps-over");
        try
        {
            var c = await _factory.CreateAuthenticatedClientAsync();
            var (status, body) = await Query(c, "category",
                new { filter = Eq("id", parentId), deep = new { children = new { } } });
            status.Should().Be(HttpStatusCode.BadRequest);
            body.Should().Contain("children");
        }
        finally { await DeleteFamily(parentId); }
    }

    [Fact]
    public async Task One_to_many_expansion_at_the_row_cap_returns_200_with_every_row()
    {
        var max = _factory.Services.GetRequiredService<StruoQueryOptions>().MaxExpandRows;
        var parentId = await SeedParentWithChildren(max, "caps-at");
        try
        {
            var c = await _factory.CreateAuthenticatedClientAsync();
            var (status, body) = await Query(c, "category",
                new { filter = Eq("id", parentId), deep = new { children = new { } } });
            status.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("data")[0].GetProperty("children").GetArrayLength().Should().Be(max);
        }
        finally { await DeleteFamily(parentId); }
    }

    [Fact]
    public async Task Many_to_many_expansion_keeps_junction_order_and_content()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var tags = new List<string>();
        foreach (var n in new[] { "caps-t1", "caps-t2", "caps-t3" })
        {
            var r = await c.PostAsJsonAsync("/api/items/tag", new { name = n });
            using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            tags.Add(d.RootElement.GetProperty("data").GetProperty("id").GetString()!);
        }
        var order = new[] { tags[2], tags[0], tags[1] };
        var created = await c.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "CapsM2M" } }, tags = order });
        using var cd = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var articleId = cd.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var (status, body) = await Query(c, "article",
            new { filter = Eq("id", articleId), deep = new { tags = new { } } });
        status.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(body);
        var got = doc.RootElement.GetProperty("data")[0].GetProperty("tags").EnumerateArray()
            .Select(t => t.GetProperty("id").GetString()).ToList();
        got.Should().Equal(order);
    }
}
