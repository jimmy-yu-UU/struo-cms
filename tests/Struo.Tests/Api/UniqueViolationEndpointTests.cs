using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Api.Http;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// A duplicate on a core unique index (roles.name) is raised by the live backend and must surface as
// 409 CONFLICT with a fixed message on both surfaces, whichever database the suite runs against.
[Collection("ApiIntegration")]
public class UniqueViolationEndpointTests(ApiFactory factory)
{
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task Duplicate_role_name_over_REST_is_409_CONFLICT_with_the_fixed_message()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var name = $"dup-{Guid.NewGuid():N}";
        (await admin.PostAsJsonAsync("/api/items/role", new { name })).StatusCode.Should().Be(HttpStatusCode.Created);

        var resp = await admin.PostAsJsonAsync("/api/items/role", new { name });

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        var error = Root(body).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("CONFLICT");
        error.GetProperty("message").GetString().Should().Be(DomainErrorMap.UniqueViolationMessage);
        body.Should().NotContain("ux_").And.NotContain(name);
    }

    [Fact]
    public async Task Duplicate_role_name_over_GraphQL_is_CONFLICT()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var name = $"dupgql-{Guid.NewGuid():N}";
        var query = $"mutation {{ createRole(input: {{ name: \"{name}\" }}) {{ id }} }}";
        var first = await admin.PostAsJsonAsync("/graphql", new { query });
        (await first.Content.ReadAsStringAsync()).Should().NotContain("\"errors\"");

        var resp = await admin.PostAsJsonAsync("/graphql", new { query });

        var body = await resp.Content.ReadAsStringAsync();
        var error = Root(body).GetProperty("errors")[0];
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("CONFLICT");
        error.GetProperty("message").GetString().Should().Be(DomainErrorMap.UniqueViolationMessage);
    }
}
