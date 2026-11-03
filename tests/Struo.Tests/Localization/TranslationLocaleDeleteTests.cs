using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

/// <summary>
/// An update deletes a whole locale's translation with <c>"&lt;locale&gt;": null</c>. The default locale
/// cannot be deleted, a locale without a stored row is a no-op, and a create treats a non-default null as
/// not sent. A revert removes the enabled non-default locales its snapshot does not have.
/// </summary>
[Collection("ApiIntegration")]
public class TranslationLocaleDeleteTests(ApiFactory factory)
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

    private static async Task<JsonElement> DataAsync(HttpClient c, string id) =>
        Root(await (await c.GetAsync($"/api/items/fxArticle/{id}")).Content.ReadAsStringAsync()).GetProperty("data");

    private static async Task<JsonElement> TranslationsAsync(HttpClient c, string id) =>
        (await DataAsync(c, id)).GetProperty("translations");

    private static Task<HttpResponseMessage> PutAsync(HttpClient c, string id, object body) =>
        c.PutAsJsonAsync($"/api/items/fxArticle/{id}", JsonSerializer.SerializeToElement(body));

    private static Dictionary<string, object?> Locales(params (string Locale, object? Value)[] entries) =>
        entries.ToDictionary(e => e.Locale, e => e.Value);

    [Fact]
    public async Task Rest_null_deletes_an_existing_non_default_locale_and_keeps_the_rest()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));

        (await PutAsync(c, id, new { translations = Locales(("zh-TW", null)) })).StatusCode.Should().Be(HttpStatusCode.OK);

        var tr = await TranslationsAsync(c, id);
        tr.TryGetProperty("zh-TW", out _).Should().BeFalse();
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("A");
    }

    [Fact]
    public async Task Rest_null_can_delete_one_locale_while_updating_another()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));

        (await PutAsync(c, id, new { translations = Locales(("en", new { title = "A2" }), ("zh-TW", null)) }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var tr = await TranslationsAsync(c, id);
        tr.TryGetProperty("zh-TW", out _).Should().BeFalse();
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("A2");
    }

    [Fact]
    public async Task Rest_null_for_a_locale_with_no_stored_row_is_a_no_op()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A" } });

        (await PutAsync(c, id, new { translations = Locales(("zh-TW", null)) })).StatusCode.Should().Be(HttpStatusCode.OK);

        var tr = await TranslationsAsync(c, id);
        tr.TryGetProperty("zh-TW", out _).Should().BeFalse();
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("A");
    }

    [Fact]
    public async Task Rest_null_for_the_default_locale_is_400_and_writes_nothing()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));

        var resp = await PutAsync(c, id, new
        {
            status = "published",
            translations = Locales(("zh-TW", null), ("en", null)),
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("default locale");
        var data = await DataAsync(c, id);
        data.GetProperty("status").GetString().Should().Be("draft");
        data.GetProperty("translations").GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z");
        data.GetProperty("translations").GetProperty("en").GetProperty("title").GetString().Should().Be("A");
    }

    [Fact]
    public async Task Rest_null_for_an_unknown_locale_is_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A" } });

        (await PutAsync(c, id, new { translations = Locales(("xx-NOPE", null)) })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rest_a_non_object_non_null_locale_value_is_still_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));

        (await PutAsync(c, id, new { translations = Locales(("zh-TW", "gone")) })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        (await TranslationsAsync(c, id)).GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z");
    }

    [Fact]
    public async Task Rest_create_treats_a_non_default_null_as_not_sent()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", null)));

        var tr = await TranslationsAsync(c, id);
        tr.TryGetProperty("zh-TW", out _).Should().BeFalse();
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("A");
    }

    [Fact]
    public async Task Rest_create_with_a_null_default_locale_is_still_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = Locales(("en", null), ("zh-TW", new { title = "Z" })),
        });

        (await c.PostAsJsonAsync("/api/items/fxArticle", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Graphql_update_that_omits_a_locale_leaves_it_stored()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));

        var resp = await c.PostAsJsonAsync("/graphql", new
        {
            query = $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ translations: [ {{ locale: \"en\", fields: {{ title: \"C\" }} }} ] }}) {{ id }} }}",
        });
        Root(await resp.Content.ReadAsStringAsync()).TryGetProperty("errors", out var errors)
            .Should().BeFalse($"unexpected errors: {errors}");

        var tr = await TranslationsAsync(c, id);
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("C");
        tr.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z");
    }

    private static async Task<JsonElement> GraphqlAsync(HttpClient c, string query) =>
        Root(await (await c.PostAsJsonAsync("/graphql", new { query })).Content.ReadAsStringAsync());

    [Fact]
    public async Task Graphql_null_fields_deletes_the_stored_locale_and_keeps_the_rest()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));

        var root = await GraphqlAsync(c,
            $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ translations: [ {{ locale: \"zh-TW\", fields: null }} ] }}) {{ id }} }}");

        root.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        var tr = await TranslationsAsync(c, id);
        tr.TryGetProperty("zh-TW", out _).Should().BeFalse();
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("A");
    }

    [Fact]
    public async Task Graphql_null_fields_for_the_default_locale_is_an_error_and_writes_nothing()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));

        var root = await GraphqlAsync(c,
            $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ translations: [ {{ locale: \"zh-TW\", fields: {{ title: \"Z2\" }} }}, {{ locale: \"en\", fields: null }} ] }}) {{ id }} }}");

        root.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("default locale");
        var tr = await TranslationsAsync(c, id);
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("A");
        tr.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z");
    }

    [Fact]
    public async Task Graphql_entry_without_fields_is_an_error_and_deletes_nothing()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));

        var root = await GraphqlAsync(c,
            $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ translations: [ {{ locale: \"zh-TW\" }} ] }}) {{ id }} }}");

        root.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("needs `fields`");
        (await TranslationsAsync(c, id)).GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z");
    }

    private static async Task<JsonElement> GraphqlVarsAsync(HttpClient c, string query, object variables) =>
        Root(await (await c.PostAsJsonAsync("/graphql", new { query, variables })).Content.ReadAsStringAsync());

    private static Dictionary<string, object?> Entry(string locale, bool withFields, object? fields = null)
    {
        var entry = new Dictionary<string, object?> { ["locale"] = locale };
        if (withFields) entry["fields"] = fields;
        return entry;
    }

    [Fact]
    public async Task Graphql_whole_input_variable_entry_without_fields_is_an_error_and_deletes_nothing()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));

        var root = await GraphqlVarsAsync(c,
            $"mutation($input: FxArticleUpdateInput!) {{ updateFxArticle(id: \"{id}\", input: $input) {{ id }} }}",
            new { input = new { translations = new[] { Entry("zh-TW", withFields: false) } } });

        root.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("needs `fields`");
        (await TranslationsAsync(c, id)).GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z");
    }

    [Fact]
    public async Task Graphql_nested_translations_variable_entry_without_fields_is_an_error_and_deletes_nothing()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));

        var root = await GraphqlVarsAsync(c,
            $"mutation($tr: [FxArticleTranslationInput!]) {{ updateFxArticle(id: \"{id}\", input: {{ translations: $tr }}) {{ id }} }}",
            new { tr = new[] { Entry("zh-TW", withFields: false) } });

        root.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("needs `fields`");
        (await TranslationsAsync(c, id)).GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z");
    }

    [Fact]
    public async Task Graphql_variable_entry_with_explicit_null_fields_deletes_the_locale()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));

        var root = await GraphqlVarsAsync(c,
            $"mutation($input: FxArticleUpdateInput!) {{ updateFxArticle(id: \"{id}\", input: $input) {{ id }} }}",
            new { input = new { translations = new[] { Entry("zh-TW", withFields: true) } } });

        root.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        (await TranslationsAsync(c, id)).TryGetProperty("zh-TW", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Revert_removes_a_locale_the_snapshot_does_not_have()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, new { en = new { title = "A" } });
        (await PutAsync(c, id, new { translations = Locales(("zh-TW", new { title = "Z" })) })).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await TranslationsAsync(c, id)).TryGetProperty("zh-TW", out _).Should().BeTrue();

        (await c.PostAsync($"/api/items/fxArticle/{id}/revisions/1/revert", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var tr = await TranslationsAsync(c, id);
        tr.TryGetProperty("zh-TW", out _).Should().BeFalse();
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("A");
    }

    [Fact]
    public async Task Revert_keeps_a_locale_the_snapshot_has()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, Locales(("en", new { title = "A" }), ("zh-TW", new { title = "Z" })));
        (await PutAsync(c, id, new { translations = Locales(("zh-TW", new { title = "Z2" })) })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        (await c.PostAsync($"/api/items/fxArticle/{id}/revisions/1/revert", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        (await TranslationsAsync(c, id)).GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z");
    }
}
