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
        (await client.GetAsync("/api/items/article")).StatusCode.Should().Be(HttpStatusCode.OK);
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
            readCollections: ["article"], writeCollections: []);
        var resp = await client.PostAsJsonAsync("/api/items/category", new { name = "X" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
