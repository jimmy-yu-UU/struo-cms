using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

[Collection("ApiIntegration")]
public class TranslationQueryTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;
    private static object Eq(object v) => new Dictionary<string, object> { ["_eq"] = v };

    private async Task<string> NewArticle(System.Net.Http.HttpClient c, string enTitle, string zhTitle)
    {
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = enTitle, body = (string?)null },
                ["zh-TW"] = new { title = zhTitle, body = (string?)null }
            }
        });
        return Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Filter_translatable_field_at_locale()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var hit = await NewArticle(c, "EnUnique", "ZhUniqueAAA");
        await NewArticle(c, "EnOther", "ZhOther");

        var env = JsonSerializer.SerializeToElement(new { filter = new Dictionary<string, object> { ["title"] = Eq("ZhUniqueAAA") } });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query?locale=zh-TW", env)).Content.ReadAsStringAsync()).GetProperty("data");
        data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).Should().Contain(hit).And.HaveCount(1);
    }

    /// <summary>DB-17: the translatable-field filter path now projects only the FK column at the SQL
    /// level instead of materializing whole translation rows. Assert it still returns EVERY matching
    /// parent — not just the first — proving the projected result set is a full, correctly-deduped
    /// list of ids rather than e.g. a single scalar.</summary>
    [Fact]
    public async Task Filter_translatable_field_matches_multiple_items()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var zhTitle = $"SharedZh-{tag}";
        var hit1 = await NewArticle(c, "EnA", zhTitle);
        var hit2 = await NewArticle(c, "EnB", zhTitle);
        await NewArticle(c, "EnC", $"Other-{tag}");

        var env = JsonSerializer.SerializeToElement(new { filter = new Dictionary<string, object> { ["title"] = Eq(zhTitle) } });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query?locale=zh-TW", env)).Content.ReadAsStringAsync()).GetProperty("data");
        var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        ids.Should().BeEquivalentTo([hit1, hit2]);
    }

    [Fact]
    public async Task Sort_translatable_field_at_locale()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        // Unique per-run titles + an _in filter isolate this assertion to exactly this test's two rows.
        // The shared ApiIntegration SQLite DB accumulates many draft articles across the suite (well
        // past the default 25-row page), so a broad `status = draft` sort would paginate one of these
        // rows off the first page — the assertion must be over an isolated set, not the whole table.
        var tag = Guid.NewGuid().ToString("N")[..8];
        var aaaTitle = $"Aaa-{tag}";
        var zzzTitle = $"Zzz-{tag}";
        var aaa = await NewArticle(c, aaaTitle, "Aaa");
        var zzz = await NewArticle(c, zzzTitle, "Zzz");
        var env = JsonSerializer.SerializeToElement(new
        {
            sort = new[] { "-title" },
            filter = new Dictionary<string, object>
            {
                ["title"] = new Dictionary<string, object> { ["_in"] = new[] { aaaTitle, zzzTitle } }
            }
        });
        var order = Root(await (await c.PostAsJsonAsync("/api/items/article/query?locale=en", env)).Content.ReadAsStringAsync())
            .GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        order.Should().Equal(zzz, aaa);   // desc by title: Zzz before Aaa, and ONLY these two rows
    }
}
