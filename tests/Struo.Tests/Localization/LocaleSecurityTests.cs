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
        var c = _factory.CreateClient();
        // A single quote in the code would break the sort subquery literal.
        var resp = await c.PostAsJsonAsync("/api/items/language",
            new { code = "x' OR '1'='1", name = "Malicious", isDefault = false, enabled = true, sort = 99 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_language_with_quote_in_code_returns_400()
    {
        var c = _factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/items/language",
            new { code = "en'", name = "BadCode", isDefault = false, enabled = true, sort = 98 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Query_sort_with_sql_injection_locale_returns_400_not_500()
    {
        var c = _factory.CreateClient();
        // A quote-bearing ?locale must be rejected before reaching the sort subquery builder.
        var body = JsonSerializer.SerializeToElement(new { sort = new[] { "title" } });
        var resp = await c.PostAsJsonAsync("/api/items/article/query?locale=en'--", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Query_with_well_formed_locale_still_works()
    {
        // Sanity: a valid locale code must not be rejected by the format guard.
        var c = _factory.CreateClient();
        var body = JsonSerializer.SerializeToElement(new { sort = new[] { "title" } });
        var resp = await c.PostAsJsonAsync("/api/items/article/query?locale=en", body);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 2. Search OR-composition: translatable-field search returns the right rows
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_on_translatable_title_returns_matching_article()
    {
        var c = _factory.CreateClient();

        // Create two articles with distinct translatable titles.
        string MakeArticle(string enTitle, string zhTitle)
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
            var resp = c.PostAsJsonAsync("/api/items/article", body).GetAwaiter().GetResult();
            return Root(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult())
                .GetProperty("data").GetProperty("id").GetString()!;
        }

        var hitId  = MakeArticle("UniqueOrSearchHit",  "UniqueOrSearchHit-zh");
        var missId = MakeArticle("UnrelatedOrMiss",    "UnrelatedOrMiss-zh");

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
        var c = _factory.CreateClient();

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
