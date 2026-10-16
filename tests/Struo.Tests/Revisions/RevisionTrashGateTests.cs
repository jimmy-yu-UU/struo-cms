// tests/Struo.Tests/Revisions/RevisionTrashGateTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Revisions;

/// <summary>Revisions of a trashed item are visible only to callers holding delete permission;
/// everyone else (and any unknown id) gets not-found, over REST and GraphQL.</summary>
[Collection("ApiIntegration")]
public class RevisionTrashGateTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private async Task<(HttpClient Admin, string Id)> CreateTrashedArticleAsync(string title)
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var create = await admin.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
        (await admin.DeleteAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (admin, id);
    }

    [Fact]
    public async Task Anonymous_gets_404_for_revisions_of_a_trashed_item()
    {
        var (_, id) = await CreateTrashedArticleAsync("TrashGateAnon");
        var anon = _factory.CreateClient();

        (await anon.GetAsync($"/api/items/article/{id}/revisions")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.GetAsync($"/api/items/article/{id}/revisions/1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reader_without_delete_permission_gets_404_for_a_trashed_item()
    {
        var (_, id) = await CreateTrashedArticleAsync("TrashGateReader");
        var (reader, _) = await _factory.CreateEditorClientAsync(
            readCollections: ["article"], writeCollections: []);

        (await reader.GetAsync($"/api/items/article/{id}/revisions")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/items/article/{id}/revisions/1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Caller_with_delete_permission_sees_revisions_of_a_trashed_item()
    {
        var (admin, id) = await CreateTrashedArticleAsync("TrashGateAdmin");

        var list = await admin.GetAsync($"/api/items/article/{id}/revisions");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await list.Content.ReadAsStringAsync()).GetProperty("data").GetArrayLength().Should().BeGreaterThan(0);
        (await admin.GetAsync($"/api/items/article/{id}/revisions/1")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_id_is_404_on_the_revision_list()
    {
        var anon = _factory.CreateClient();
        var resp = await anon.GetAsync($"/api/items/article/{Guid.CreateVersion7()}/revisions");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Live_item_revisions_stay_readable_by_a_reader()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var create = await admin.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "TrashGateLive" } } });
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
        var (reader, _) = await _factory.CreateEditorClientAsync(
            readCollections: ["article"], writeCollections: []);

        (await reader.GetAsync($"/api/items/article/{id}/revisions")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/items/article/{id}/revisions/1")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GraphQl_anonymous_gets_no_revisions_for_a_trashed_item()
    {
        var (_, id) = await CreateTrashedArticleAsync("TrashGateGql");
        var anon = _factory.CreateClient();

        var list = await anon.PostAsJsonAsync("/graphql",
            new { query = $"{{ articleRevisions(id: \"{id}\") {{ revisionNumber }} }}" });
        var listRoot = Root(await list.Content.ReadAsStringAsync());
        listRoot.TryGetProperty("errors", out _).Should().BeFalse();
        listRoot.GetProperty("data").GetProperty("articleRevisions").GetArrayLength().Should().Be(0);

        var single = await anon.PostAsJsonAsync("/graphql",
            new { query = $"{{ articleRevision(id: \"{id}\", revisionNumber: 1) {{ revisionNumber }} }}" });
        var singleRoot = Root(await single.Content.ReadAsStringAsync());
        singleRoot.TryGetProperty("errors", out _).Should().BeFalse();
        singleRoot.GetProperty("data").GetProperty("articleRevision").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
