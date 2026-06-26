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

    private async Task<long> NewArticle(System.Net.Http.HttpClient c, string enTitle, string zhTitle)
    {
        var author = Root(await (await c.PostAsJsonAsync("/api/items/author", new { name = "QA" })).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft", authorId = author,
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = enTitle, body = (string?)null },
                ["zh-TW"] = new { title = zhTitle, body = (string?)null }
            }
        });
        return Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();
    }

    [Fact]
    public async Task Filter_translatable_field_at_locale()
    {
        var c = _factory.CreateClient();
        var hit = await NewArticle(c, "EnUnique", "ZhUniqueAAA");
        await NewArticle(c, "EnOther", "ZhOther");

        var env = JsonSerializer.SerializeToElement(new { filter = new Dictionary<string, object> { ["title"] = Eq("ZhUniqueAAA") } });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query?locale=zh-TW", env)).Content.ReadAsStringAsync()).GetProperty("data");
        data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).Should().Contain(hit).And.HaveCount(1);
    }

    [Fact]
    public async Task Sort_translatable_field_at_locale()
    {
        var c = _factory.CreateClient();
        var aaa = await NewArticle(c, "Aaa", "Aaa");
        var zzz = await NewArticle(c, "Zzz", "Zzz");
        var env = JsonSerializer.SerializeToElement(new { sort = new[] { "-title" }, filter = new Dictionary<string, object> { ["status"] = Eq("draft") } });
        var order = Root(await (await c.PostAsJsonAsync("/api/items/article/query?locale=en", env)).Content.ReadAsStringAsync())
            .GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        order.IndexOf(zzz).Should().BeLessThan(order.IndexOf(aaa));
    }
}
