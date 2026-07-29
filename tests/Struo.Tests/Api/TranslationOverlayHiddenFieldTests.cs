// tests/Struo.Tests/Api/TranslationOverlayHiddenFieldTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// <see cref="Struo.Application.Query.TranslationOverlay"/> builds its
/// <c>translations.{locale}</c> map from ALL of a translation sidecar's fields, with no Hidden check —
/// unlike <see cref="Struo.Application.Query.Projection.ItemProjector"/> (guards top-level fields) and
/// <see cref="Struo.Application.Query.RevisionSnapshotRedactor"/> (guards revision snapshots). This is
/// the third and last leak in the "Hidden field never reaches an external caller" guarantee family.
/// Article's <c>internalSlug</c> (Hidden + Translatable, on <c>ArticleTranslation</c>) is the existing
/// existing fixture, reused here for the plain REST item-read path (GET /api/items/article/{id}), which
/// goes through <c>ItemService.GetAsync</c> -> <c>ItemProjector.Project</c> -> <c>TranslationOverlay.ApplyAsync</c>.
/// </summary>
[Collection("ApiIntegration")]
public class TranslationOverlayHiddenFieldTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task Get_item_excludes_hidden_translatable_field_from_translations_map()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/items/article", new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "OverlayHiddenFieldTest", internalSlug = "secret-en" },
                ["zh-TW"] = new { title = "隱藏欄位測試", internalSlug = "secret-zh" },
            }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var get = await client.GetAsync($"/api/items/article/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await get.Content.ReadAsStringAsync();

        // Hidden+Translatable field must not appear anywhere in the response, under any locale.
        body.Should().NotContain("internalSlug");
        body.Should().NotContain("secret-en");
        body.Should().NotContain("secret-zh");

        var translations = Root(body).GetProperty("data").GetProperty("translations");
        translations.GetProperty("en").TryGetProperty("internalSlug", out _).Should().BeFalse();
        translations.GetProperty("zh-TW").TryGetProperty("internalSlug", out _).Should().BeFalse();

        // Non-hidden translatable fields must still be present and correct.
        translations.GetProperty("en").GetProperty("title").GetString().Should().Be("OverlayHiddenFieldTest");
        translations.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("隱藏欄位測試");
    }

    /// <summary>Same leak, other overlay call site: <c>ItemService.QueryAsync</c> (list/GET-collection
    /// path, ItemService.cs:69) shares the exact same <c>TranslationOverlay.ApplyAsync</c> call as the
    /// single-item GET tested above, but is a distinct code path (goes through <c>ItemProjector.Project</c>
    /// per-row, not the single-entity overload) -- cheap to pin given the two call sites could drift.</summary>
    [Fact]
    public async Task List_items_excludes_hidden_translatable_field_from_translations_map()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/items/article", new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "OverlayHiddenFieldListTest", internalSlug = "list-secret-en" },
                ["zh-TW"] = new { title = "隱藏欄位列表測試", internalSlug = "list-secret-zh" },
            }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var list = await client.GetAsync($"/api/items/article?filter[id][_eq]={id}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await list.Content.ReadAsStringAsync();

        body.Should().NotContain("internalSlug");
        body.Should().NotContain("list-secret-en");
        body.Should().NotContain("list-secret-zh");

        var data = Root(body).GetProperty("data");
        data.GetArrayLength().Should().Be(1);
        var translations = data[0].GetProperty("translations");
        translations.GetProperty("en").TryGetProperty("internalSlug", out _).Should().BeFalse();
        translations.GetProperty("zh-TW").TryGetProperty("internalSlug", out _).Should().BeFalse();
        translations.GetProperty("en").GetProperty("title").GetString().Should().Be("OverlayHiddenFieldListTest");
        translations.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("隱藏欄位列表測試");
    }
}
