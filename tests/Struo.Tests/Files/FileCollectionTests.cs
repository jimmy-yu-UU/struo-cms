using System.Linq;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Infrastructure.Metadata;
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
        var c = await _factory.CreateAuthenticatedClientAsync(); // /api/schema now requires auth
        var resp = await c.GetAsync("/api/schema/file");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fields = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("fields");
        var names = fields.EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        names.Should().Contain(new[] { "fileName", "contentType", "size", "status", "title", "alt" });
        fields.EnumerateArray().Should().Contain(f =>
            f.GetProperty("name").GetString() == "title" && f.GetProperty("translatable").GetBoolean());
    }

    // #12: File soft-delete + retire `archived`.
    [Fact]
    public void File_is_soft_deletable()
    {
        typeof(Struo.Domain.Auditing.ISoftDeletable)
            .IsAssignableFrom(typeof(Struo.Infrastructure.Files.File))
            .Should().BeTrue();
    }

    [Fact]
    public void File_status_options_no_longer_include_archived()
    {
        var meta = MetadataScanner.ScanTypes([typeof(Struo.Infrastructure.Files.File)])
            .Single(c => string.Equals(c.Name, "file", StringComparison.OrdinalIgnoreCase));
        var status = meta.Fields.Single(f => f.Name == "status");
        status.Options!.Select(o => o.Value).Should().BeEquivalentTo(["draft", "published"]);
        meta.SoftDelete.Should().BeTrue();
    }
}
