using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class AuthEnforcementTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task Anonymous_write_is_401()
    {
        var c = _factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "X" } } });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_write_stamps_real_user()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var create = await admin.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "Y" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        Root(await create.Content.ReadAsStringAsync()).GetProperty("data")
            .GetProperty("createdBy").GetString().Should().Be(_factory.AdminUserId.ToString());
    }

    [Fact]
    public async Task Anonymous_content_read_allowed_but_user_read_requires_auth()
    {
        var c = _factory.CreateClient();
        (await c.GetAsync("/api/items/article")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetAsync("/api/items/user")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
