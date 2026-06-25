// tests/Struo.Tests/Api/ItemsEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class ItemsEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task Crud_round_trip_with_envelope_and_audit()
    {
        var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/items/article", new { title = "Hello", status = "draft" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdData = Root(await create.Content.ReadAsStringAsync()).GetProperty("data");
        var id = createdData.GetProperty("id").GetInt64();
        createdData.GetProperty("createdBy").GetString().Should().Be("system");

        var get = await client.GetAsync($"/api/items/article/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var put = await client.PutAsJsonAsync($"/api/items/article/{id}", new { title = "Updated", status = "published" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await put.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("title").GetString().Should().Be("Updated");

        (await client.DeleteAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_filters_sorts_paginates_with_meta()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < 4; i++)
            await client.PostAsJsonAsync("/api/items/article", new { title = $"Post{i}", status = i % 2 == 0 ? "published" : "draft" });

        var resp = await client.GetAsync("/api/items/article?filter[status][_eq]=published&sort=-title&limit=1&offset=0&fields=id,title");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.GetProperty("meta").GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        var first = root.GetProperty("data")[0];
        first.TryGetProperty("status", out _).Should().BeFalse();
        first.TryGetProperty("title", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_field_returns_400()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/items/article?filter[ghost][_eq]=x");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_collection_returns_404()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/api/items/nope")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Filter_by_id_returns_the_row()
    {
        var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/items/article", new { title = "IdFilterTest", status = "draft" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetInt64();

        var resp = await client.GetAsync($"/api/items/article?filter[id][_eq]={id}&fields=id,title");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        data.GetArrayLength().Should().Be(1);
        data[0].GetProperty("id").GetInt64().Should().Be(id);
    }

    [Fact]
    public async Task Sort_by_id_descending_succeeds()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/items/article?sort=-id&limit=2");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
