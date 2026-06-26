// tests/Struo.Tests/Query/RelationWriteTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class RelationWriteTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;
    private static async Task<long> Id(System.Net.Http.HttpResponseMessage r) =>
        Root(await r.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetInt64();

    [Fact]
    public async Task Create_article_with_tags_syncs_junction_and_deep_reads_them()
    {
        var c = _factory.CreateClient();
        var authorId = await Id(await c.PostAsJsonAsync("/api/items/author", new { name = "A" }));
        var t1 = await Id(await c.PostAsJsonAsync("/api/items/tag", new { name = "t1" }));
        var t2 = await Id(await c.PostAsJsonAsync("/api/items/tag", new { name = "t2" }));

        var id = await Id(await c.PostAsJsonAsync("/api/items/article", new { title = "M2M", status = "draft", authorId, tags = new[] { t1, t2 } }));

        var tags = Root(await (await c.GetAsync($"/api/items/article/{id}?deep=tags")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("tags");
        tags.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Update_article_with_empty_tags_clears_junction()
    {
        var c = _factory.CreateClient();
        var authorId = await Id(await c.PostAsJsonAsync("/api/items/author", new { name = "B" }));
        var t1 = await Id(await c.PostAsJsonAsync("/api/items/tag", new { name = "c1" }));
        var t2 = await Id(await c.PostAsJsonAsync("/api/items/tag", new { name = "c2" }));
        var id = await Id(await c.PostAsJsonAsync("/api/items/article", new { title = "Clear", status = "draft", authorId, tags = new[] { t1, t2 } }));

        // sanity: 2 tags assigned
        Root(await (await c.GetAsync($"/api/items/article/{id}?deep=tags")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("tags").GetArrayLength().Should().Be(2);

        // update with an explicit empty tags array -> junction cleared
        (await c.PutAsJsonAsync($"/api/items/article/{id}", new { title = "Clear", status = "draft", authorId, tags = Array.Empty<long>() }))
            .EnsureSuccessStatusCode();

        Root(await (await c.GetAsync($"/api/items/article/{id}?deep=tags")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("tags").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Delete_author_referenced_by_article_is_blocked_409()
    {
        var c = _factory.CreateClient();
        var authorId = await Id(await c.PostAsJsonAsync("/api/items/author", new { name = "Ref" }));
        await c.PostAsJsonAsync("/api/items/article", new { title = "R", status = "draft", authorId });

        (await c.DeleteAsync($"/api/items/author/{authorId}")).StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Delete_unreferenced_author_returns_204()
    {
        var c = _factory.CreateClient();
        var authorId = await Id(await c.PostAsJsonAsync("/api/items/author", new { name = "Unreferenced" }));

        (await c.DeleteAsync($"/api/items/author/{authorId}")).StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }
}
