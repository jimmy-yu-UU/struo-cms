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
}
