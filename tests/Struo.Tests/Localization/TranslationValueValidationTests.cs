using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

/// <summary>
/// Translation values bind to the sidecar property types with the same rules as root fields, and a
/// <c>translations</c> object names each locale once.
/// </summary>
[Collection("ApiIntegration")]
public class TranslationValueValidationTests(ApiFactory factory)
{
    private const string DuplicateMessage = "Locale 'en' appears more than once in translations.";
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static JsonElement Body(object translations) =>
        JsonSerializer.SerializeToElement(new { status = "draft", translations });

    private static StringContent Json(string raw) => new(raw, Encoding.UTF8, "application/json");

    private static async Task<string> CreateAsync(HttpClient c)
    {
        var resp = await c.PostAsJsonAsync("/api/items/fxArticle", Body(new { en = new { title = "A" } }));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> GetAsync(HttpClient c, string id) =>
        Root(await c.GetStringAsync($"/api/items/fxArticle/{id}")).GetProperty("data");

    public static TheoryData<string, string> WrongShapes => new()
    {
        { "title", "5" },
        { "title", "{\"a\":1}" },
        { "title", "[1,2]" },
        { "title", "true" },
        { "seoOgImageId", "5" },
        { "seoOgImageId", "\"not-a-guid\"" },
    };

    [Theory]
    [MemberData(nameof(WrongShapes))]
    public async Task Wrong_shaped_value_is_rejected_with_field_and_locale(string field, string rawJson)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var title = field == "title" ? "" : "\"title\":\"A\",";
        var raw = $"{{\"status\":\"draft\",\"translations\":{{\"en\":{{{title}\"{field}\":{rawJson}}}}}}}";

        var resp = await c.PostAsync("/api/items/fxArticle", Json(raw));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = Root(await resp.Content.ReadAsStringAsync()).GetProperty("error");
        error.GetProperty("message").GetString().Should().Be($"Field '{field}' has an invalid value for locale 'en'.");
        var detail = error.GetProperty("details").EnumerateArray().Single();
        detail.GetProperty("field").GetString().Should().Be(field);
        detail.GetProperty("locale").GetString().Should().Be("en");
    }

    [Fact]
    public async Task Valid_values_null_and_rich_text_keep_their_behavior()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var og = Guid.NewGuid().ToString();
        var resp = await c.PostAsJsonAsync("/api/items/fxArticle", Body(new
        {
            en = new { title = "T", body = "<p>ok</p><script>alert(1)</script>", seoOgImageId = og, seoTitle = (string?)null },
        }));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var en = (await GetAsync(c, id)).GetProperty("translations").GetProperty("en");
        en.GetProperty("title").GetString().Should().Be("T");
        en.GetProperty("body").GetString().Should().Contain("<p>ok</p>").And.NotContain("script");
        en.GetProperty("seoOgImageId").GetString().Should().Be(og);

        var clear = await c.PutAsJsonAsync($"/api/items/fxArticle/{id}",
            JsonSerializer.SerializeToElement(new { translations = new { en = new { seoOgImageId = (string?)null } } }));
        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync(c, id)).GetProperty("translations").GetProperty("en")
            .GetProperty("seoOgImageId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Translatable_json_field_read_back_unchanged_is_stored_unchanged()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var created = await c.PostAsync("/api/items/fxArticle", Json(
            "{\"status\":\"draft\",\"translations\":{\"en\":{\"title\":\"A\",\"facts\":{\"a\":1}}}}"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await created.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
        var facts = (await GetAsync(c, id)).GetProperty("translations").GetProperty("en").GetProperty("facts");

        var put = await c.PutAsJsonAsync($"/api/items/fxArticle/{id}",
            JsonSerializer.SerializeToElement(new { translations = new { en = new { facts } } }));

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync(c, id)).GetProperty("translations").GetProperty("en")
            .GetProperty("facts").GetRawText().Should().Be("{\"a\":1}");
    }

    public static TheoryData<string> DuplicateBodies => new()
    {
        "{\"en\":{\"title\":\"B\"},\"EN\":{\"title\":\"C\"}}",
        "{\"en\":{\"title\":\"B\"},\"en\":{\"title\":\"C\"}}",
        "{\"en\":{\"title\":\"B\"},\"EN\":null}",
    };

    [Theory]
    [MemberData(nameof(DuplicateBodies))]
    public async Task Duplicate_locale_on_update_is_rejected_and_nothing_is_written(string translationsJson)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c);

        var resp = await c.PutAsync($"/api/items/fxArticle/{id}", Json($"{{\"translations\":{translationsJson}}}"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = Root(await resp.Content.ReadAsStringAsync()).GetProperty("error");
        error.GetProperty("message").GetString().Should().Be(DuplicateMessage);
        error.GetProperty("details").EnumerateArray().Single().GetProperty("locale").GetString().Should().Be("en");
        (await GetAsync(c, id)).GetProperty("translations").GetProperty("en")
            .GetProperty("title").GetString().Should().Be("A");
    }

    [Fact]
    public async Task Duplicate_locale_with_root_change_rolls_the_whole_write_back()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c);

        var resp = await c.PutAsync($"/api/items/fxArticle/{id}", Json(
            "{\"status\":\"published\",\"translations\":{\"en\":{\"title\":\"B\"},\"EN\":{\"title\":\"C\"}}}"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetAsync(c, id)).GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task Duplicate_locale_on_create_is_rejected()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var resp = await c.PostAsync("/api/items/fxArticle", Json(
            "{\"status\":\"draft\",\"translations\":{\"en\":{\"title\":\"B\"},\"EN\":{\"title\":\"C\"}}}"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("error").GetProperty("message").GetString()
            .Should().Be(DuplicateMessage);
    }

    [Fact]
    public async Task Graphql_exact_duplicate_locale_errors()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var resp = await c.PostAsJsonAsync("/graphql", new
        {
            query = "mutation { createFxArticle(input: { translations: [ " +
                    "{ locale: \"en\", fields: { title: \"a\" } }, { locale: \"en\", fields: { title: \"b\" } } ] }) { id } }",
        });

        var err = Root(await resp.Content.ReadAsStringAsync()).GetProperty("errors")[0];
        err.GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        err.GetProperty("message").GetString().Should().Be(DuplicateMessage);
    }
}
