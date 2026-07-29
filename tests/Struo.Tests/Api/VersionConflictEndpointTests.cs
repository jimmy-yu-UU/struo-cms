// tests/Struo.Tests/Api/VersionConflictEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// An optimistic-lock miss (a stale-version update) must surface over the REST pipeline as
/// HTTP 409 with the SPLIT code <c>VERSION_CONFLICT</c> — not the generic <c>CONFLICT</c> that a
/// relation-restrict delete or a duplicate-key inline failure carries. This is the full-pipeline
/// counterpart to the in-process GraphQL proof in
/// <c>GraphQlMutationExecutionTests.Update_version_conflict_maps_to_VERSION_CONFLICT</c>; together
/// they pin the code on BOTH protocols (REST/GraphQL parity).
///
/// Stale-version recipe: create (version 0) → update echoing version 0 (server bumps to 1) →
/// update again still echoing the OLD version 0 → CAS matches zero rows → 409 VERSION_CONFLICT.
/// </summary>
[Collection("ApiIntegration")]
public class VersionConflictEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task Rest_stale_version_update_is_409_VERSION_CONFLICT()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "V" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = Root(await create.Content.ReadAsStringAsync()).GetProperty("data");
        var id = created.GetProperty("id").GetString()!;
        var version = created.GetProperty("version").GetInt64(); // 0 at creation

        // First update echoing the loaded version succeeds and advances the row's version.
        var ok = await client.PutAsJsonAsync($"/api/items/article/{id}",
            new { status = "published", version });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second update still echoing the OLD version is a CAS miss.
        var stale = await client.PutAsJsonAsync($"/api/items/article/{id}",
            new { status = "draft", version });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Root(await stale.Content.ReadAsStringAsync())
            .GetProperty("error").GetProperty("code").GetString().Should().Be("VERSION_CONFLICT");
    }
}
