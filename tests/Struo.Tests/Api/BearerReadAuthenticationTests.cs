using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// Read endpoints (ItemsController's List/Query/Get/Revisions and /graphql) carry no [Authorize], so
// UseAuthentication used to run only the DEFAULT scheme. With Cookies as the default, an
// Authorization: Bearer header was never inspected on those endpoints and the per-request permission
// snapshot fell back to the public floor. A forwarding policy scheme makes the default adaptive, so a
// bearer caller is authenticated everywhere and gets its OWN grants.
[Collection("ApiIntegration")]
public class BearerReadAuthenticationTests(ApiFactory factory)
{
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private async Task<HttpClient> BearerClientForAsync(HttpClient admin, Guid userId)
    {
        var gen = await admin.PostAsync($"/api/users/{userId}/access-token", null);
        gen.StatusCode.Should().Be(HttpStatusCode.OK, await gen.Content.ReadAsStringAsync());
        var token = Root(await gen.Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("token").GetString()!;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Bearer_token_authenticates_a_read_of_a_collection_only_its_role_grants()
    {
        // 'mediaFolder' is NOT in ApiFactory's Rbac:PublicReadCollections, so the public floor cannot
        // explain a 200 here — only the token's own role can.
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (_, userId) = await factory.CreateEditorClientAsync(
            readCollections: ["mediaFolder"], writeCollections: []);
        var bearer = await BearerClientForAsync(admin, userId);

        var resp = await bearer.GetAsync("/api/items/mediaFolder");

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Bearer_token_authenticates_a_graphql_query()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (_, userId) = await factory.CreateEditorClientAsync(
            readCollections: ["mediaFolder"], writeCollections: []);
        var bearer = await BearerClientForAsync(admin, userId);

        var resp = await bearer.PostAsJsonAsync("/graphql", new
        {
            query = "{ mediaFolders { items { id } } }"
        });

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, body);
        Root(body).TryGetProperty("errors", out _).Should().BeFalse(body);
    }

    [Fact]
    public async Task Bearer_caller_keeps_the_public_floor_on_a_collection_its_role_omits()
    {
        // The fix must not narrow anyone: 'category' is public-read and this role never mentions it.
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (_, userId) = await factory.CreateEditorClientAsync(
            readCollections: ["mediaFolder"], writeCollections: []);
        var bearer = await BearerClientForAsync(admin, userId);

        (await bearer.GetAsync("/api/items/category")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_read_of_a_public_collection_is_unaffected()
    {
        var anon = factory.CreateClient();

        (await anon.GetAsync("/api/items/category")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
