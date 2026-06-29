using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

// NOTE: Gallery_m2m_with_guid_file_ids_round_trips was deleted in Phase 5.5
// (ArticleFile junction entity removed from the sample domain).
// NOTE: Single_image_relation_expands was deleted in Phase 5.6
// (parent Article.SeoOgImage relation removed; SEO moved to SeoTranslation sidecar).

[Collection("ApiIntegration")]
public class FileReferenceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task File_upload_and_info_round_trip()
    {
        // Smoke-test that the files subsystem is wired up in the integration host.
        var c = _factory.CreateClient();
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var mp = new MultipartFormDataContent { { content, "file", "smoke.bin" } };
        var resp = await c.PostAsync("/api/files", mp);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var body = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("data").GetProperty("id").GetString().Should().NotBeNullOrEmpty();
    }
}
