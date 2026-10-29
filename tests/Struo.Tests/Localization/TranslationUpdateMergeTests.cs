using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

/// <summary>
/// An update merges the sent translation fields onto the stored row of each locale: a field the request
/// omits keeps its stored value, an explicit null clears it, and a locale without a stored row is inserted.
/// Required translatable fields are judged on the merged value on update and on the sent value on create.
/// </summary>
[Collection("ApiIntegration")]
public class TranslationUpdateMergeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static async Task<string> CreateAsync(HttpClient c, object translations)
    {
        var body = JsonSerializer.SerializeToElement(new { status = "draft", translations });
        var resp = await c.PostAsJsonAsync("/api/items/fxArticle", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> TranslationsAsync(HttpClient c, string id) =>
        Root(await (await c.GetAsync($"/api/items/fxArticle/{id}")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("translations");

    private static Task<HttpResponseMessage> PutTranslationsAsync(HttpClient c, string id, object translations) =>
        c.PutAsJsonAsync($"/api/items/fxArticle/{id}",
            JsonSerializer.SerializeToElement(new { translations }));

    [Fact]
    public async Task Rest_update_keeps_a_field_the_request_omits()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A", body = "B" } });

        (await PutTranslationsAsync(c, id, new { en = new { title = "C" } })).StatusCode.Should().Be(HttpStatusCode.OK);

        var en = (await TranslationsAsync(c, id)).GetProperty("en");
        en.GetProperty("title").GetString().Should().Be("C");
        en.GetProperty("body").GetString().Should().Be("B");
    }

    [Fact]
    public async Task Rest_update_with_explicit_null_clears_the_field()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A", body = "B" } });

        (await PutTranslationsAsync(c, id, new { en = new { body = (string?)null } })).StatusCode.Should().Be(HttpStatusCode.OK);

        var en = (await TranslationsAsync(c, id)).GetProperty("en");
        en.GetProperty("title").GetString().Should().Be("A");
        en.GetProperty("body").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Rest_update_inserts_a_locale_that_has_no_stored_row_and_keeps_the_others()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A", body = "B" } });

        (await PutTranslationsAsync(c, id, new Dictionary<string, object> { ["zh-TW"] = new { title = "Z" } })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var tr = await TranslationsAsync(c, id);
        tr.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z");
        tr.GetProperty("en").GetProperty("body").GetString().Should().Be("B");
    }

    [Fact]
    public async Task Rest_update_omitting_a_filled_required_field_succeeds()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A", body = "B" } });

        (await PutTranslationsAsync(c, id, new { en = new { body = "B2" } })).StatusCode.Should().Be(HttpStatusCode.OK);

        var en = (await TranslationsAsync(c, id)).GetProperty("en");
        en.GetProperty("title").GetString().Should().Be("A");
        en.GetProperty("body").GetString().Should().Be("B2");
    }

    [Fact]
    public async Task Rest_update_sending_null_for_a_required_field_is_400_and_changes_nothing()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A", body = "B" } });

        (await PutTranslationsAsync(c, id, new { en = new { title = (string?)null } })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        (await TranslationsAsync(c, id)).GetProperty("en").GetProperty("title").GetString().Should().Be("A");
    }

    [Fact]
    public async Task Rest_update_of_a_new_locale_must_carry_every_required_field()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A" } });

        (await PutTranslationsAsync(c, id, new Dictionary<string, object> { ["zh-TW"] = new { body = "only body" } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rest_create_still_requires_every_required_field_in_every_sent_locale()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "A" },
                ["zh-TW"] = new { body = "no title" },
            },
        });

        (await c.PostAsJsonAsync("/api/items/fxArticle", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Graphql_update_keeps_a_field_the_request_omits()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A", body = "B" } });

        var resp = await c.PostAsJsonAsync("/graphql", new
        {
            query = $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ translations: [ {{ locale: \"en\", fields: {{ title: \"C\" }} }} ] }}) {{ id }} }}",
        });
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");

        var en = (await TranslationsAsync(c, id)).GetProperty("en");
        en.GetProperty("title").GetString().Should().Be("C");
        en.GetProperty("body").GetString().Should().Be("B");
    }

    [Fact]
    public async Task Revert_restores_every_translation_field_of_the_snapshot()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A", body = "B" } });
        (await PutTranslationsAsync(c, id, new { en = new { title = "C", body = "D" } })).StatusCode.Should().Be(HttpStatusCode.OK);

        var revert = await c.PostAsync($"/api/items/fxArticle/{id}/revisions/1/revert", null);
        revert.StatusCode.Should().Be(HttpStatusCode.OK);

        var en = (await TranslationsAsync(c, id)).GetProperty("en");
        en.GetProperty("title").GetString().Should().Be("A");
        en.GetProperty("body").GetString().Should().Be("B");
    }

    [Fact]
    public async Task Revert_restores_a_field_that_was_null_in_the_snapshot_to_null()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A" } });
        (await PutTranslationsAsync(c, id, new { en = new { body = "filled later" } })).StatusCode.Should().Be(HttpStatusCode.OK);

        var revert = await c.PostAsync($"/api/items/fxArticle/{id}/revisions/1/revert", null);
        revert.StatusCode.Should().Be(HttpStatusCode.OK);

        var en = (await TranslationsAsync(c, id)).GetProperty("en");
        en.GetProperty("title").GetString().Should().Be("A");
        en.GetProperty("body").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
