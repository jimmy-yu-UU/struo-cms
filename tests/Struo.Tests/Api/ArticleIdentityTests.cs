// tests/Struo.Tests/Api/ArticleIdentityTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class ArticleIdentityTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_article_returns_a_guid_id_and_is_fetchable()
    {
        var body = new
        {
            status = "draft",
            translations = new { en = new { title = "Hello", body = "World" } }
        };

        var create = await _client.PostAsJsonAsync("/api/items/article", body);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var idStr = created.RootElement.GetProperty("data").GetProperty("id").GetString();

        Guid.TryParse(idStr, out var id).Should().BeTrue();
        id.Version.Should().Be(7);

        var get = await _client.GetAsync($"/api/items/article/{idStr}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
