// tests/Struo.Tests/Api/RbacEnforcementTests.cs
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class RbacEnforcementTests(ApiFactory factory)
{
    [Fact]
    public async Task Anonymous_can_read_a_public_granted_collection()
    {
        var client = factory.CreateClient(); // anonymous → public role grants article
        (await client.GetAsync("/api/items/fxArticle")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_reading_an_ungranted_collection_is_401()
    {
        var client = factory.CreateClient();
        (await client.GetAsync("/api/items/user")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SuperAdmin_can_read_any_collection()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        (await client.GetAsync("/api/items/user")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authenticated_editor_writing_an_ungranted_collection_is_403()
    {
        var (client, _) = await factory.CreateEditorClientAsync(
            readCollections: ["fxArticle"], writeCollections: []);
        var resp = await client.PostAsJsonAsync("/api/items/fxCategory", new { name = "X" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Roleless_authenticated_user_gets_public_read_but_no_write()
    {
        var (client, _) = await factory.CreateRolelessClientAsync();
        // 'fxArticle' is granted to public via ApiFactory config → role-less user inherits it.
        (await client.GetAsync("/api/items/fxArticle")).StatusCode.Should().Be(HttpStatusCode.OK);
        // public has no write on 'fxCategory' → forbidden (authenticated → 403).
        var write = await client.PostAsJsonAsync("/api/items/fxCategory", new { name = "X" });
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authenticated_editor_reads_a_public_granted_collection_it_was_not_granted()
    {
        // 'fxCategory' is public-read via ApiFactory config; this editor's role only mentions 'fxArticle'.
        // public is a FLOOR, so being signed in must never read LESS than being anonymous.
        var (client, _) = await factory.CreateEditorClientAsync(
            readCollections: ["fxArticle"], writeCollections: ["fxArticle"]);

        (await client.GetAsync("/api/items/fxCategory")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
