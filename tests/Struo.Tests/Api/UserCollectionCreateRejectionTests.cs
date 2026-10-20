using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// User rows carry a password hash and sessions, which only POST /api/users sets up. The generic
// create path would insert a user nobody can sign in as, so it is refused on both surfaces, before
// any duplicate-email check.
[Collection("ApiIntegration")]
public class UserCollectionCreateRejectionTests(ApiFactory factory)
{
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task Generic_create_on_the_user_collection_is_a_400_pointing_at_the_users_endpoint()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var resp = await admin.PostAsJsonAsync("/api/items/user", new { email = $"nope{Guid.NewGuid():N}@x.com" });

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        var error = Root(body).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Contain("POST /api/users");
    }

    [Fact]
    public async Task Generic_create_of_an_existing_email_is_still_a_400_not_a_409()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var email = $"dup{Guid.NewGuid():N}@x.com";
        (await admin.PostAsJsonAsync("/api/users", new { email, password = "pw12345678" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var resp = await admin.PostAsJsonAsync("/api/items/user", new { email = email.ToUpperInvariant() });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GraphQl_createUser_is_rejected_the_same_way()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var resp = await admin.PostAsJsonAsync("/graphql", new
        {
            query = "mutation { createUser(input: { email: \"gql@x.com\" }) { id } }"
        });

        var body = await resp.Content.ReadAsStringAsync();
        Root(body).GetProperty("errors").EnumerateArray().Should().NotBeEmpty(body);
        body.Should().Contain("BAD_USER_INPUT").And.Contain("POST /api/users");
    }
}
