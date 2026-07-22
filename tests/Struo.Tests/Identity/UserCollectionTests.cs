using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class UserCollectionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task User_schema_does_not_expose_password_or_accessToken()
    {
        var c = await _factory.CreateAuthenticatedClientAsync(); // SEC-8: /api/schema now requires auth
        var resp = await c.GetAsync("/api/schema/user");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fields = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("fields");
        var names = fields.EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        names.Should().Contain("email");
        names.Should().NotContain("password");     // Hidden
        names.Should().NotContain("accessToken");   // Hidden
    }
}
