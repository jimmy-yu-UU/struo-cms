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

        var create = await client.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "Hello" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdData = Root(await create.Content.ReadAsStringAsync()).GetProperty("data");
        var id = createdData.GetProperty("id").GetString()!;
        createdData.GetProperty("createdBy").GetString().Should().Be(Guid.Empty.ToString());

        var get = await client.GetAsync($"/api/items/article/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var put = await client.PutAsJsonAsync($"/api/items/article/{id}",
            new { status = "published", translations = new { en = new { title = "Updated" } } });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await put.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("status").GetString().Should().Be("published");

        (await client.DeleteAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_filters_sorts_paginates_with_meta()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < 4; i++)
            await client.PostAsJsonAsync("/api/items/article",
                new { status = i % 2 == 0 ? "published" : "draft", translations = new { en = new { title = $"Post{i}" } } });

        // NOTE: sort/filter/fields by the translatable `title` is locale-aware querying (Task 5).
        // Here we filter+sort+select by non-translatable own-collection fields (status, id).
        var resp = await client.GetAsync("/api/items/article?filter[status][_eq]=published&sort=-id&limit=1&offset=0&fields=id,status");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.GetProperty("meta").GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        var first = root.GetProperty("data")[0];
        first.TryGetProperty("status", out _).Should().BeTrue();
        first.TryGetProperty("id", out _).Should().BeTrue();
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

        var create = await client.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "IdFilterTest" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var resp = await client.GetAsync($"/api/items/article?filter[id][_eq]={id}&fields=id,status");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        data.GetArrayLength().Should().Be(1);
        data[0].GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Sort_by_id_descending_succeeds()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/items/article?sort=-id&limit=2");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
