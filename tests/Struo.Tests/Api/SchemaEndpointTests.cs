// tests/Struo.Tests/Api/SchemaEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class SchemaEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Schema_lists_all_collections()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(); // /api/schema now requires auth
        var response = await client.GetAsync("/api/schema");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"name\":\"article\"");
        body.Should().Contain("\"name\":\"category\"");
    }

    [Fact]
    public async Task Schema_for_collection_includes_interfaces_options_and_seo()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(); // /api/schema now requires auth
        var response = await client.GetAsync("/api/schema/article");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"interface\":\"richText\"");   // enum as camelCase string
        body.Should().Contain("\"translatable\":true");
        body.Should().Contain("\"value\":\"draft\"");          // option pair
        body.Should().Contain("\"name\":\"seoTitle\"");        // SEO from SeoTranslation sidecar (translatable)
        body.Should().Contain("\"isSystem\":true");            // audit fields

        // A prior change deliberately changed seoOgImageId interface from Hidden -> Image; guard against reversion.
        // Parse as JsonDocument so we assert the "image" interface belongs specifically to the seoOgImageId field
        // (a global string scan would pass even if some other field carried the image interface).
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var fields = doc.RootElement.GetProperty("data").GetProperty("fields");
        var seoOgImageField = fields.EnumerateArray()
            .FirstOrDefault(f => f.TryGetProperty("name", out var n) && n.GetString() == "seoOgImageId");
        seoOgImageField.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object,
            "seoOgImageId field should be present in the schema");
        seoOgImageField.GetProperty("interface").GetString().Should().Be("image");
    }

    [Fact]
    public async Task Schema_for_unknown_collection_returns_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(); // /api/schema now requires auth
        var response = await client.GetAsync("/api/schema/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Schema_serializes_the_hidden_collection_flag()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var json = await (await client.GetAsync("/api/schema")).Content.ReadFromJsonAsync<JsonElement>();
        var collections = json.GetProperty("data").EnumerateArray().ToList();

        collections.First(c => c.GetProperty("name").GetString() == "permission")
            .GetProperty("hidden").GetBoolean().Should().BeTrue();
        collections.First(c => c.GetProperty("name").GetString() == "role")
            .GetProperty("hidden").GetBoolean().Should().BeFalse();
    }
}
