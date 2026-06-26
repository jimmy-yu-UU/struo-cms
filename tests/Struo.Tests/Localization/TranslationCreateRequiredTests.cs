using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

/// <summary>
/// Regression coverage for Spec §10: creating a row on a translatable collection without the
/// default-locale translation must fail with 400. Update keeps partial-update semantics (an absent
/// <c>translations</c> payload is a no-op and existing translations are preserved).
/// </summary>
[Collection("ApiIntegration")]
public class TranslationCreateRequiredTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<long> NewAuthor(System.Net.Http.HttpClient c, string n) =>
        Root(await (await c.PostAsJsonAsync("/api/items/author", new { name = n })).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

    [Fact]
    public async Task Create_without_translations_returns_400()
    {
        var c = _factory.CreateClient();
        var author = await NewAuthor(c, "CR-A");

        // No `translations` key at all → the default-locale translation is missing.
        var body = JsonSerializer.SerializeToElement(new { status = "draft", authorId = author });

        (await c.PostAsJsonAsync("/api/items/article", body)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_missing_default_locale_returns_400()
    {
        var c = _factory.CreateClient();
        var author = await NewAuthor(c, "CR-B");

        // Translations present, but only the non-default locale (default is "en").
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft", authorId = author,
            translations = new Dictionary<string, object>
            {
                ["zh-TW"] = new { title = "只中", body = (string?)null }
            }
        });

        (await c.PostAsJsonAsync("/api/items/article", body)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_without_translations_is_allowed()
    {
        var c = _factory.CreateClient();
        var author = await NewAuthor(c, "CR-C");

        // 1. Create a valid article carrying the default-locale translation.
        var createBody = JsonSerializer.SerializeToElement(new
        {
            status = "draft", authorId = author,
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "Keep-Me", body = "B-en" }
            }
        });
        var id = Root(await (await c.PostAsJsonAsync("/api/items/article", createBody)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

        // 2. PUT with no `translations` key — just a status change. Must NOT 400.
        var updBody = JsonSerializer.SerializeToElement(new { status = "published", authorId = author });
        var updResp = await c.PutAsJsonAsync($"/api/items/article/{id}", updBody);
        updResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. The existing translation is preserved (partial update did not clear it).
        var tr = Root(await (await c.GetAsync($"/api/items/article/{id}")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("translations");
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("Keep-Me");
    }

    [Fact]
    public async Task Create_with_default_locale_translation_returns_201()
    {
        var c = _factory.CreateClient();
        var author = await NewAuthor(c, "CR-D");

        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft", authorId = author,
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "Valid", body = "B-en" }
            }
        });

        (await c.PostAsJsonAsync("/api/items/article", body)).StatusCode
            .Should().Be(HttpStatusCode.Created);
    }
}
