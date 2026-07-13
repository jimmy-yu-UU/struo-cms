using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class NestedListArgsExpansionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> Post(HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    [Fact]
    public async Task QueryWhereInFiltered_narrows_by_extra_filter()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "RepoFilterCat" });
        await Post(c, "article", new { status = "published", categoryId = cat, translations = new { en = new { title = "Pub" } } });
        await Post(c, "article", new { status = "draft", categoryId = cat, translations = new { en = new { title = "Drf" } } });

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IItemRepository>();

        var filter = new ComparisonFilter("status", QueryOperator.Eq, "published");
        var rows = await repo.QueryWhereInFilteredAsync(
            "article", "categoryId", new object[] { System.Guid.Parse(cat) }, filter);

        rows.Should().HaveCount(1);
    }

    [Fact]
    public async Task Nested_o2m_filter_narrows_list()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "O2MFilterCat" });
        await Post(c, "article", new { status = "published", categoryId = cat, translations = new { en = new { title = "Pub1" } } });
        await Post(c, "article", new { status = "draft", categoryId = cat, translations = new { en = new { title = "Drf1" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = cat } },
            deep = new { articles = new { filter = new { status = new Dictionary<string, object> { ["_eq"] = "published" } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        row.GetProperty("articles").GetArrayLength().Should().Be(1); // draft filtered out
    }

    [Fact]
    public async Task Nested_m2m_filter_narrows_list()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var tagAi = await Post(c, "tag", new { name = "AI" });
        var tagUx = await Post(c, "tag", new { name = "UX" });
        var art = await Post(c, "article", new
        {
            status = "draft", tags = new[] { tagAi, tagUx },
            translations = new { en = new { title = "M2MFilterArt" } }
        });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = art } },
            deep = new { tags = new { filter = new { name = new Dictionary<string, object> { ["_eq"] = "AI" } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        var tags = row.GetProperty("tags");
        tags.GetArrayLength().Should().Be(1);
        tags[0].GetProperty("name").GetString().Should().Be("AI");
    }
}
