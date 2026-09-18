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
        var client = await _factory.CreateAuthenticatedClientAsync(); // /api/schema now requires auth
        var body = await (await client.GetAsync("/api/schema/article")).Content.ReadAsStringAsync();
        body.Should().Contain("\"relations\"");
        body.Should().Contain("\"name\":\"category\"");
        body.Should().Contain("\"kind\":\"manyToOne\"");
        // seoOgImage parent relation removed (SEO moved to SeoTranslation sidecar)
        body.Should().NotContain("\"name\":\"seoOgImage\"");
        body.Should().Contain("\"targetCollection\":\"category\"");

        // Removed Author/ArticleFile entities.
        // Tag/ArticleTag M2M reintroduced for TagSelect live-verification.
        body.Should().NotContain("\"name\":\"author\"");
        body.Should().Contain("\"name\":\"tags\"");
        body.Should().Contain("\"kind\":\"manyToMany\"");
        body.Should().Contain("\"targetCollection\":\"tag\"");

        // "gallery" is intentionally present in the body as a scalar Files *field* (not a
        // relation) — a whole-body substring check for its absence would conflict with that, so
        // assert against the parsed relations array specifically, guarding against the old
        // ArticleFile *relation* of that name reappearing.
        using var doc = JsonDocument.Parse(body);
        var relationNames = doc.RootElement.GetProperty("data").GetProperty("relations")
            .EnumerateArray()
            .Select(r => r.GetProperty("name").GetString());
        relationNames.Should().NotContain("gallery");
    }

    [Fact]
    public async Task Category_schema_includes_self_referencing_tree()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(); // /api/schema now requires auth
        var body = await (await client.GetAsync("/api/schema/category")).Content.ReadAsStringAsync();
        body.Should().Contain("\"name\":\"parent\"");
        body.Should().Contain("\"selfReferencing\":true");
        body.Should().Contain("\"name\":\"children\"");
    }

    [Fact]
    public async Task Article_tags_relation_exposes_junction_payload_fields_and_sort_field()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var body = await (await client.GetAsync("/api/schema/article")).Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var relations = doc.RootElement.GetProperty("data").GetProperty("relations").EnumerateArray().ToList();
        var tags = relations.Single(r => r.GetProperty("name").GetString() == "tags");
        tags.GetProperty("junctionCollection").GetString().Should().Be("articleTag");
        tags.GetProperty("sortField").GetString().Should().Be("sort");
        tags.GetProperty("junctionPayloadFields").EnumerateArray().Select(e => e.GetString()).Should().Equal("note");
        var category = relations.Single(r => r.GetProperty("name").GetString() == "category");
        category.GetProperty("sortField").ValueKind.Should().Be(JsonValueKind.Null);
        category.GetProperty("junctionPayloadFields").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
