// tests/Struo.Tests/Api/RevisionEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class RevisionEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private static async Task<string> CreateArticleAsync(HttpClient client, string title, string status = "draft")
    {
        var create = await client.PostAsJsonAsync("/api/items/article",
            new { status, translations = new { en = new { title } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        return Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Revisions_lifecycle_over_rest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateArticleAsync(client, "RevisionRoundTrip", status: "draft"); // rev 1: create, status=draft

        var put = await client.PutAsJsonAsync($"/api/items/article/{id}",
            new { status = "published", translations = new { en = new { title = "RevisionRoundTrip" } } });
        put.StatusCode.Should().Be(HttpStatusCode.OK); // rev 2: update, status=published

        // List is newest-first: [update, create].
        var list1 = await client.GetAsync($"/api/items/article/{id}/revisions");
        list1.StatusCode.Should().Be(HttpStatusCode.OK);
        var data1 = Root(await list1.Content.ReadAsStringAsync()).GetProperty("data");
        data1.GetArrayLength().Should().Be(2);
        data1[0].GetProperty("operation").GetString().Should().Be("update");
        data1[0].GetProperty("revisionNumber").GetInt64().Should().Be(2);
        data1[1].GetProperty("operation").GetString().Should().Be("create");
        data1[1].GetProperty("revisionNumber").GetInt64().Should().Be(1);

        // Get revision 1 -> structured snapshot (not a quoted string) reflecting the draft state.
        var rev1 = await client.GetAsync($"/api/items/article/{id}/revisions/1");
        rev1.StatusCode.Should().Be(HttpStatusCode.OK);
        var rev1Data = Root(await rev1.Content.ReadAsStringAsync()).GetProperty("data");
        rev1Data.GetProperty("operation").GetString().Should().Be("create");
        var snapshot = rev1Data.GetProperty("snapshot");
        snapshot.ValueKind.Should().Be(JsonValueKind.Object);
        snapshot.GetProperty("status").GetString().Should().Be("draft");

        // Revert to rev 1 -> current item reflects the draft snapshot again.
        var revert = await client.PostAsync($"/api/items/article/{id}/revisions/1/revert", null);
        revert.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await revert.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("status").GetString()
            .Should().Be("draft");

        var get = await client.GetAsync($"/api/items/article/{id}");
        Root(await get.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("status").GetString()
            .Should().Be("draft");

        // Revert appends a new revision (append-only) -> now 3, newest "revert".
        var list2 = await client.GetAsync($"/api/items/article/{id}/revisions");
        var data2 = Root(await list2.Content.ReadAsStringAsync()).GetProperty("data");
        data2.GetArrayLength().Should().Be(3);
        data2[0].GetProperty("operation").GetString().Should().Be("revert");
        data2[0].GetProperty("revisionNumber").GetInt64().Should().Be(3);
    }

    /// <summary>The REST revision-get endpoint must not leak Article's Hidden own-field
    /// (<c>internalNote</c>) or its Hidden+Translatable field (<c>internalSlug</c>, nested under every
    /// <c>translations.{locale}</c>), even though both were captured in full by the snapshot builder.</summary>
    [Fact]
    public async Task Get_revision_redacts_hidden_fields()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/items/article", new
        {
            status = "draft",
            internalNote = "secret-token",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "HiddenFieldRedaction", internalSlug = "secret-en" },
                ["zh-TW"] = new { title = "隱藏欄位", internalSlug = "secret-zh" },
            }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var rev1 = await client.GetAsync($"/api/items/article/{id}/revisions/1");
        rev1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await rev1.Content.ReadAsStringAsync();
        body.Should().NotContain("secret-token");
        body.Should().NotContain("secret-en");
        body.Should().NotContain("secret-zh");
        body.Should().NotContain("internalNote");
        body.Should().NotContain("internalSlug");

        var snapshot = Root(body).GetProperty("data").GetProperty("snapshot");
        snapshot.GetProperty("status").GetString().Should().Be("draft"); // non-hidden fields untouched
    }

    [Fact]
    public async Task Get_unknown_revision_is_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateArticleAsync(client, "UnknownRevisionTarget");

        var resp = await client.GetAsync($"/api/items/article/{id}/revisions/999");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revert_requires_write_permission()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateArticleAsync(admin, "NeedsWriteToRevert");

        var (editor, _) = await _factory.CreateEditorClientAsync(
            readCollections: ["article"], writeCollections: []);

        var resp = await editor.PostAsync($"/api/items/article/{id}/revisions/1/revert", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
