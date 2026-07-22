// tests/Struo.Tests/Api/RelationSchemaTests.cs
using System.Linq;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class RelationSchemaTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Article_schema_includes_relations()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(); // SEC-8: /api/schema now requires auth
        var body = await (await client.GetAsync("/api/schema/article")).Content.ReadAsStringAsync();
        body.Should().Contain("\"relations\"");
        body.Should().Contain("\"name\":\"category\"");
        body.Should().Contain("\"kind\":\"manyToOne\"");
        // seoOgImage parent relation removed in Phase 5.6 (SEO moved to SeoTranslation sidecar)
        body.Should().NotContain("\"name\":\"seoOgImage\"");
        body.Should().Contain("\"targetCollection\":\"category\"");

        // Phase-5.5 realignment: removed Author/ArticleFile entities.
        // Phase 7d: Tag/ArticleTag M2M reintroduced for TagSelect live-verification.
        body.Should().NotContain("\"name\":\"author\"");
        body.Should().Contain("\"name\":\"tags\"");
        body.Should().Contain("\"kind\":\"manyToMany\"");
        body.Should().Contain("\"targetCollection\":\"tag\"");

        // Phase-5.5 note above guarded against the old ArticleFile *relation* named "gallery"
        // reappearing. Phase 7g+ Task 4 intentionally reintroduces "gallery" as a scalar Files
        // *field* (not a relation), so a whole-body substring check is no longer valid; assert
        // against the parsed relations array specifically instead.
        using var doc = JsonDocument.Parse(body);
        var relationNames = doc.RootElement.GetProperty("data").GetProperty("relations")
            .EnumerateArray()
            .Select(r => r.GetProperty("name").GetString());
        relationNames.Should().NotContain("gallery");
    }

    [Fact]
    public async Task Category_schema_includes_self_referencing_tree()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(); // SEC-8: /api/schema now requires auth
        var body = await (await client.GetAsync("/api/schema/category")).Content.ReadAsStringAsync();
        body.Should().Contain("\"name\":\"parent\"");
        body.Should().Contain("\"selfReferencing\":true");
        body.Should().Contain("\"name\":\"children\"");
    }
}
