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
        var client = await _factory.CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/items/fxArticle",
            new { status = "draft", translations = new { en = new { title = "Hello" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdData = Root(await create.Content.ReadAsStringAsync()).GetProperty("data");
        var id = createdData.GetProperty("id").GetString()!;
        createdData.GetProperty("createdBy").GetString().Should().Be(_factory.AdminUserId.ToString());

        var get = await client.GetAsync($"/api/items/fxArticle/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var put = await client.PutAsJsonAsync($"/api/items/fxArticle/{id}",
            new { status = "published", translations = new { en = new { title = "Updated" } } });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await put.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("status").GetString().Should().Be("published");

        (await client.DeleteAsync($"/api/items/fxArticle/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/items/fxArticle/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_filters_sorts_paginates_with_meta()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var published = new List<string>();
        var all = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var isPublished = i % 2 == 0;
            var create = await client.PostAsJsonAsync("/api/items/fxArticle",
                new { status = isPublished ? "published" : "draft", translations = new { en = new { title = $"Post{i}" } } });
            create.StatusCode.Should().Be(HttpStatusCode.Created);
            var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
            all.Add(id);
            if (isPublished) published.Add(id);
        }

        // NOTE: sort/filter/fields by the translatable `title` is locale-aware querying.
        // Here we filter+sort+select by non-translatable own-collection fields (status, id), scoped to
        // this test's four rows so the totals do not depend on what other tests left in the collection.
        var scope = $"filter[id][_in]={string.Join(',', all)}&filter[status][_eq]=published&limit=1&offset=0&fields=id,status";

        var desc = await client.GetAsync($"/api/items/fxArticle?{scope}&sort=-id");
        desc.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = Root(await desc.Content.ReadAsStringAsync());
        root.GetProperty("meta").GetProperty("total").GetInt32().Should().Be(2, "the status filter keeps only the two published rows");
        var data = root.GetProperty("data");
        data.GetArrayLength().Should().Be(1, "limit=1");
        var first = data[0];
        first.GetProperty("status").GetString().Should().Be("published");
        published.Should().Contain(first.GetProperty("id").GetString());
        // Ids are time-ordered, so the later published row is the larger one (SQL Server orders uuids differently).
        if (TestBackend.Current != TestBackendKind.SqlServer)
            first.GetProperty("id").GetString().Should().Be(published.Max(StringComparer.Ordinal), "sort=-id puts the largest id first");
        first.TryGetProperty("createdAt", out _).Should().BeFalse("fields=id,status projects the other own fields away");

        var asc = await client.GetAsync($"/api/items/fxArticle?{scope}&sort=id");
        asc.StatusCode.Should().Be(HttpStatusCode.OK);
        var ascFirstId = Root(await asc.Content.ReadAsStringAsync()).GetProperty("data")[0].GetProperty("id").GetString();
        published.Should().Contain(ascFirstId);
        ascFirstId.Should().NotBe(first.GetProperty("id").GetString(), "sort direction decides which published row comes first");
    }

    [Fact]
    public async Task Filter_by_id_returns_the_row()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/items/fxArticle",
            new { status = "draft", translations = new { en = new { title = "IdFilterTest" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var resp = await client.GetAsync($"/api/items/fxArticle?filter[id][_eq]={id}&fields=id,status");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        data.GetArrayLength().Should().Be(1);
        data[0].GetProperty("id").GetString().Should().Be(id);
    }
}
