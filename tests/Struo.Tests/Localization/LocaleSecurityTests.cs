// tests/Struo.Tests/Localization/LocaleSecurityTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

/// <summary>
/// Regression tests for:
///   1. SQL injection guard — locale codes with SQL metacharacters are rejected at the boundary.
///   2. Search OR-composition — translatable-field search returns correct rows via the OR group.
/// </summary>
[Collection("ApiIntegration")]
public class LocaleSecurityTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    // ──────────────────────────────────────────────────────────────────────
    // 1. Injection guard: language code with a single quote is rejected (400)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_language_with_sql_injection_code_returns_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        // A single quote in the code would break the sort subquery literal.
        var resp = await c.PostAsJsonAsync("/api/items/language",
            new { code = "x' OR '1'='1", name = "Malicious", isDefault = false, enabled = true, sort = 99 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_language_with_quote_in_code_returns_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var resp = await c.PostAsJsonAsync("/api/items/language",
            new { code = "en'", name = "BadCode", isDefault = false, enabled = true, sort = 98 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static StringContent RawJson(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    private static async Task<bool> LanguageExistsAsync(HttpClient c, string code)
    {
        var list = Root(await (await c.GetAsync("/api/items/language?limit=200")).Content.ReadAsStringAsync());
        return list.GetProperty("data").EnumerateArray()
            .Any(r => string.Equals(r.GetProperty("code").GetString(), code, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_language_with_capitalised_Code_key_is_validated()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var bad = "x' OR 1=1 #";
        var resp = await c.PostAsync("/api/items/language",
            RawJson("{\"Code\":\"x' OR 1=1 #\",\"Name\":\"X\"}"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LanguageExistsAsync(c, bad)).Should().BeFalse();
    }

    [Fact]
    public async Task Update_language_with_duplicate_case_variant_keys_is_validated()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var create = await c.PostAsync("/api/items/language",
            RawJson("{\"code\":\"zz-keep\",\"name\":\"Keep\",\"enabled\":false,\"sort\":97}"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetRawText();

        var resp = await c.PutAsync($"/api/items/language/{id}",
            RawJson("{\"code\":\"en\",\"Code\":\"bad code\"}"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LanguageExistsAsync(c, "zz-keep")).Should().BeTrue();
        (await LanguageExistsAsync(c, "bad code")).Should().BeFalse();
    }

    [Fact]
    public async Task Create_language_with_valid_capitalised_Code_key_succeeds()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var resp = await c.PostAsync("/api/items/language",
            RawJson("{\"Code\":\"fr-CA\",\"Name\":\"French\",\"Enabled\":false}"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        (await LanguageExistsAsync(c, "fr-CA")).Should().BeTrue();
    }

    private static readonly string[] SortByTitle = ["title"];

    [Fact]
    public async Task Query_sort_with_sql_injection_locale_returns_400_not_500()
    {
        var c = _factory.CreateClient();
        // A quote-bearing ?locale must be rejected before reaching the sort subquery builder.
        var body = JsonSerializer.SerializeToElement(new { sort = SortByTitle });
        var resp = await c.PostAsJsonAsync("/api/items/article/query?locale=en'--", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Query_with_well_formed_locale_still_works()
    {
        // Sanity: a valid locale code must not be rejected by the format guard.
        var c = _factory.CreateClient();
        var body = JsonSerializer.SerializeToElement(new { sort = SortByTitle });
        var resp = await c.PostAsJsonAsync("/api/items/article/query?locale=en", body);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 2. Search OR-composition: translatable-field search returns the right rows
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_on_translatable_title_returns_matching_article()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        // Create two articles with distinct translatable titles.
        async Task<string> MakeArticle(string enTitle, string zhTitle)
        {
            var body = JsonSerializer.SerializeToElement(new
            {
                status = "draft",
                translations = new Dictionary<string, object>
                {
                    ["en"]    = new { title = enTitle,  body = (string?)null },
                    ["zh-TW"] = new { title = zhTitle,  body = (string?)null }
                }
            });
            var resp = await c.PostAsJsonAsync("/api/items/article", body);
            return Root(await resp.Content.ReadAsStringAsync())
                .GetProperty("data").GetProperty("id").GetString()!;
        }

        var hitId  = await MakeArticle("UniqueOrSearchHit",  "UniqueOrSearchHit-zh");
        var missId = await MakeArticle("UnrelatedOrMiss",    "UnrelatedOrMiss-zh");

        // Search at en locale — should find only the "hit" article.
        var qBody = JsonSerializer.SerializeToElement(new { search = "UniqueOrSearchHit" });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query?locale=en", qBody))
            .Content.ReadAsStringAsync()).GetProperty("data");

        var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        ids.Should().Contain(hitId);
        ids.Should().NotContain(missId);
    }

    [Fact]
    public async Task Search_on_translatable_title_zh_returns_matching_article()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"]    = new { title = "ZhSearchTestEn",  body = (string?)null },
                ["zh-TW"] = new { title = "獨特搜尋目標",     body = (string?)null }
            }
        });
        var hitId = Root(await (await c.PostAsJsonAsync("/api/items/article", body))
            .Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var qBody = JsonSerializer.SerializeToElement(new { search = "獨特搜尋目標" });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query?locale=zh-TW", qBody))
            .Content.ReadAsStringAsync()).GetProperty("data");

        data.EnumerateArray().Select(r => r.GetProperty("id").GetString())
            .Should().Contain(hitId);
    }
}
