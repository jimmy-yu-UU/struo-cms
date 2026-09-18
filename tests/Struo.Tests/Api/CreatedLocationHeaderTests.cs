using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// EnvelopeResultFilter must not rebuild a CreatedResult as a plain ObjectResult — doing so would
// silently drop the Location header CreatedResult writes during its own execution.
[Collection("ApiIntegration")]
public class CreatedLocationHeaderTests(ApiFactory factory)
{
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task Creating_an_item_sends_a_Location_header_pointing_at_the_new_row()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var resp = await admin.PostAsJsonAsync("/api/items/mediaFolder", new { name = "loc-hdr-probe" });

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.Created, body);
        var id = Root(body).GetProperty("data").GetProperty("id").GetString();
        resp.Headers.Location!.OriginalString.Should().Be($"/api/items/mediaFolder/{id}");
        // The envelope itself is unchanged.
        Root(body).GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Creating_a_user_sends_a_Location_header()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var email = $"loc-hdr-{Guid.CreateVersion7():N}@struo.test";

        var resp = await admin.PostAsJsonAsync("/api/users", new
        {
            email, password = "loc-hdr-pw-123", name = "Location Probe"
        });

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.Created, body);
        var id = Root(body).GetProperty("data").GetProperty("id").GetString();
        resp.Headers.Location!.OriginalString.Should().Be($"/api/items/user/{id}");
    }

    [Fact]
    public async Task Uploading_a_file_sends_a_Location_header()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("location-probe"));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent { { content, "file", "location-probe.txt" } };

        var resp = await admin.PostAsync("/api/files", form);

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.Created, body);
        var id = Root(body).GetProperty("data").GetProperty("id").GetString();
        resp.Headers.Location!.OriginalString.Should().Be($"/api/files/{id}");
    }
}
