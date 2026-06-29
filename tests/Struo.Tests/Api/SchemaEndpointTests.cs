// tests/Struo.Tests/Api/SchemaEndpointTests.cs
using System.Net;
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
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/schema");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"name\":\"article\"");
        body.Should().Contain("\"name\":\"category\"");
    }

    [Fact]
    public async Task Schema_for_collection_includes_interfaces_options_and_seo()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/schema/article");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"interface\":\"richText\"");   // enum as camelCase string
        body.Should().Contain("\"translatable\":true");
        body.Should().Contain("\"value\":\"draft\"");          // option pair
        body.Should().Contain("\"name\":\"seoTitle\"");        // SEO from SeoTranslation sidecar (translatable)
        body.Should().Contain("\"isSystem\":true");            // audit fields

        // Phase 5.6 deliberately changed seoOgImageId interface from Hidden -> Image; guard against reversion.
        body.Should().Contain("\"name\":\"seoOgImageId\"");
        body.Should().Contain("\"interface\":\"image\"");
    }

    [Fact]
    public async Task Schema_for_unknown_collection_returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/schema/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
