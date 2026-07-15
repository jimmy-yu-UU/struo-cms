using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// ARC-3: REST and GraphQL must agree on the error code for an <b>unauthenticated</b>
/// <c>PermissionDeniedException</c>. Both drive the same scenario — an anonymous caller requesting
/// soft-deleted rows (<c>deleted=with</c> / <c>deleted: WITH</c>) on the public-read <c>article</c>
/// collection, which trips the viewing-deleted delete-permission gate. REST semantics govern:
/// anonymous ⇒ <c>UNAUTHORIZED</c> (not <c>FORBIDDEN</c>). Before the drift fix GraphQL answered
/// <c>FORBIDDEN</c> here (no authenticated split), so the GraphQL case is the genuine RED.
/// </summary>
[Collection("ApiIntegration")]
public class UnauthorizedDriftTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task Rest_anonymous_viewing_deleted_is_UNAUTHORIZED()
    {
        var client = _factory.CreateClient(); // anonymous; article is public-read in test config
        var resp = await client.GetAsync("/api/items/article?deleted=with");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Root(await resp.Content.ReadAsStringAsync())
            .GetProperty("error").GetProperty("code").GetString().Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task GraphQl_anonymous_viewing_deleted_is_UNAUTHORIZED()
    {
        var client = _factory.CreateClient(); // anonymous
        var resp = await client.PostAsJsonAsync(
            "/graphql", new { query = "{ articles(deleted: WITH) { total } }" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK); // GraphQL keeps HTTP 200 semantics
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.TryGetProperty("errors", out var errors).Should().BeTrue();
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        code.Should().Be("UNAUTHORIZED");
    }
}
