// tests/Struo.Tests/Api/RelationSchemaTests.cs
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
        var client = _factory.CreateClient();
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
        body.Should().NotContain("\"name\":\"gallery\"");
    }

    [Fact]
    public async Task Category_schema_includes_self_referencing_tree()
    {
        var client = _factory.CreateClient();
        var body = await (await client.GetAsync("/api/schema/category")).Content.ReadAsStringAsync();
        body.Should().Contain("\"name\":\"parent\"");
        body.Should().Contain("\"selfReferencing\":true");
        body.Should().Contain("\"name\":\"children\"");
    }
}
