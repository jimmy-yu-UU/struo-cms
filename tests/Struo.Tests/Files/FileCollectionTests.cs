using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class FileCollectionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task File_schema_lists_metadata_and_translatable_fields()
    {
        var c = _factory.CreateClient();
        var resp = await c.GetAsync("/api/schema/file");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fields = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("fields");
        var names = fields.EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        names.Should().Contain(new[] { "fileName", "contentType", "size", "status", "title", "alt" });
        fields.EnumerateArray().Should().Contain(f =>
            f.GetProperty("name").GetString() == "title" && f.GetProperty("translatable").GetBoolean());
    }
}
