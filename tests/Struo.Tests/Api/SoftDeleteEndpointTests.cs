// tests/Struo.Tests/Api/SoftDeleteEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class SoftDeleteEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private static async Task<string> CreateArticleAsync(HttpClient client, string title)
    {
        var create = await client.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        return Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Delete_soft_deletes_then_list_excludes_then_only_shows_then_restore_reappears()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateArticleAsync(client, "SoftDeleteRoundTrip");

        (await client.DeleteAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Default list excludes the soft-deleted row.
        var excluded = await client.GetAsync($"/api/items/article?filter[id][_eq]={id}");
        excluded.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await excluded.Content.ReadAsStringAsync()).GetProperty("data").GetArrayLength().Should().Be(0);

        // ?deleted=only surfaces it.
        var only = await client.GetAsync($"/api/items/article?filter[id][_eq]={id}&deleted=only");
        only.StatusCode.Should().Be(HttpStatusCode.OK);
        var onlyData = Root(await only.Content.ReadAsStringAsync()).GetProperty("data");
        onlyData.GetArrayLength().Should().Be(1);
        onlyData[0].GetProperty("id").GetString().Should().Be(id);

        // Restore brings it back.
        var restore = await client.PostAsync($"/api/items/article/{id}/restore", null);
        restore.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await restore.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString().Should().Be(id);

        var reappeared = await client.GetAsync($"/api/items/article?filter[id][_eq]={id}");
        reappeared.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await reappeared.Content.ReadAsStringAsync()).GetProperty("data").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Purge_removes_permanently()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateArticleAsync(client, "PurgeMe");

        (await client.DeleteAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/items/article/{id}?purge=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var withDeleted = await client.GetAsync($"/api/items/article/{id}?deleted=with");
        withDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleted_only_requires_delete_permission()
    {
        var (client, _) = await _factory.CreateEditorClientAsync(
            readCollections: ["article"], writeCollections: []);

        var resp = await client.GetAsync("/api/items/article?deleted=only");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_deleted_value_is_400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.GetAsync("/api/items/article?deleted=banana");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
