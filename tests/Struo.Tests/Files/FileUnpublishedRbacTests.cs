using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

// Non-published file content/metadata must not be readable merely because the caller is
// authenticated — a genuine per-collection CanWrite("file") grant is required. A role-less JIT/SSO
// user could otherwise fetch any draft file. 404 (not 403) so the endpoint does not leak the
// existence of unpublished assets.
//
// NOTE on the test harness: ApiFactory seeds "file" into Rbac:PublicReadCollections, and the "public"
// role is a FLOOR for every caller (SqlSugarRolePermissionStore) — so CanRead("file") is true for
// anonymous and authenticated callers alike and cannot gate anything. The gate is the "file" WRITE
// grant: drafts are an editorial state, and no public role is given write.
[Collection("ApiIntegration")]
public class FileUnpublishedRbacTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static async Task<string> UploadDraftAsync(HttpClient admin)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("secret-bytes"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var mp = new MultipartFormDataContent { { content, "file", "draft.txt" } };
        var up = await admin.PostAsync("/api/files", mp);
        up.StatusCode.Should().Be(HttpStatusCode.Created, await up.Content.ReadAsStringAsync());
        var id = Root(await up.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var setDraft = await admin.PutAsJsonAsync($"/api/items/file/{id}", new
        {
            status = "draft",
            translations = new Dictionary<string, object> { ["en"] = new { title = "T", alt = (string?)null } }
        });
        setDraft.StatusCode.Should().Be(HttpStatusCode.OK, await setDraft.Content.ReadAsStringAsync());
        return id;
    }

    [Fact]
    public async Task Nonpublished_denied_to_authenticated_user_without_file_write()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadDraftAsync(admin);

        // Authenticated, has a role, inherits the public read floor on "file" — but NO "file" write grant.
        var (editor, _) = await _factory.CreateEditorClientAsync(
            readCollections: ["article"], writeCollections: ["article"]);

        (await editor.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await editor.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nonpublished_visible_to_user_with_file_write()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadDraftAsync(admin);

        var (reader, _) = await _factory.CreateEditorClientAsync(
            readCollections: ["file"], writeCollections: ["file"]);

        (await reader.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var contentResp = await reader.GetAsync($"/api/files/{id}/content");
        contentResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await contentResp.Content.ReadAsStringAsync()).Should().Be("secret-bytes");
    }

    [Fact]
    public async Task Nonpublished_denied_to_anonymous()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadDraftAsync(admin);

        var anon = _factory.CreateClient();
        (await anon.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Bearer path: Get/Download carry no [Authorize], so UseAuthentication only ran the default cookie
    // scheme and the per-request permission snapshot reflects anonymous for a bearer-only caller. The
    // gate must adopt the token's principal and enforce ITS real CanRead("file") grant — otherwise any
    // bearer token would inherit the public floor and read drafts.
    private async Task<HttpClient> BearerClientForAsync(HttpClient admin, Guid userId)
    {
        var gen = await admin.PostAsync($"/api/users/{userId}/access-token", null);
        gen.StatusCode.Should().Be(HttpStatusCode.OK, await gen.Content.ReadAsStringAsync());
        var token = Root(await gen.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("token").GetString()!;
        var bearer = _factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return bearer;
    }

    [Fact]
    public async Task Nonpublished_denied_to_bearer_caller_without_file_write()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadDraftAsync(admin);

        var (_, editorId) = await _factory.CreateEditorClientAsync(
            readCollections: ["article"], writeCollections: ["article"]);
        var bearer = await BearerClientForAsync(admin, editorId);

        (await bearer.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bearer.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nonpublished_visible_to_bearer_caller_with_file_write()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadDraftAsync(admin);

        var (_, readerId) = await _factory.CreateEditorClientAsync(
            readCollections: ["file"], writeCollections: ["file"]);
        var bearer = await BearerClientForAsync(admin, readerId);

        (await bearer.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var contentResp = await bearer.GetAsync($"/api/files/{id}/content");
        contentResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await contentResp.Content.ReadAsStringAsync()).Should().Be("secret-bytes");
    }
}
