using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// File rows are owned by the upload pipeline: StorageKey, Size, ContentType and the image dimensions
// are all derived from the blob. The generic create path cannot supply them - FileName is ReadOnly, so
// ItemDeserializer nulled it into a NOT NULL column and the request 500'd. Reject it explicitly
// instead, on both surfaces.
[Collection("ApiIntegration")]
public class FileCollectionCreateRejectionTests(ApiFactory factory)
{
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task Generic_create_on_the_file_collection_is_a_client_error_not_a_500()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var resp = await admin.PostAsJsonAsync("/api/items/file", new { title = "nope" });

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        var error = Root(body).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Contain("/api/files");
    }

    [Fact]
    public async Task GraphQl_createFile_is_rejected_the_same_way()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var resp = await admin.PostAsJsonAsync("/graphql", new
        {
            query = "mutation { createFile(input: { status: \"draft\" }) { id } }"
        });

        var body = await resp.Content.ReadAsStringAsync();
        var errors = Root(body).GetProperty("errors");
        errors.EnumerateArray().Should().NotBeEmpty(body);
        body.Should().Contain("BAD_USER_INPUT");
    }

    [Fact]
    public async Task Updating_a_file_through_the_items_api_still_works()
    {
        // The admin media UI edits per-locale Title/Alt through PUT /api/items/file/{id}. That path
        // must be untouched by the create guard.
        var admin = await factory.CreateAuthenticatedClientAsync();
        var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("update-probe"));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent { { content, "file", "update-probe.txt" } };
        var up = await admin.PostAsync("/api/files", form);
        var id = Root(await up.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var put = await admin.PutAsJsonAsync($"/api/items/file/{id}", new
        {
            status = "published",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "Updated title", alt = (string?)null }
            }
        });

        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
    }
}
