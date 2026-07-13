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
}
