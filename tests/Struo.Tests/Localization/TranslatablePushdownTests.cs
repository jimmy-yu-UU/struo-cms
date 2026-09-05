// tests/Struo.Tests/Localization/TranslatablePushdownTests.cs
//
// Locks the translatable-leaf pushdown behaviour Task 4/5 already shipped (FilterTranslator.Leaf /
// TranslationSubquery / RelationPath.Chain): an own-collection translatable leaf resolves at the query
// locale (defaulted by ItemService when `locale` is absent), a translatable leaf reached across a
// relation hop (both the dotted "each-exists" form and the `_some` same-row form) resolves through the
// SAME translation-sidecar subquery even when the ROOT collection (category) has no translation
// sidecar of its own, and the translatable-field search union (FilterTranslator.SearchGroup) still
// finds rows by a translated field. All three ran GREEN on first try — the underlying pushdown already
// unconditionally computes `queryLocale` (ItemService.QueryAsync) and threads it through
// RelationPath.Chain's terminal AppendModel call, so no production change was needed here; see
// task-6-report.md for the full RED/GREEN story.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

[Collection("ApiIntegration")]
public class TranslatablePushdownTests(ApiFactory factory)
{
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static async Task<string> Post(HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    private static async Task<List<string?>> Ids(HttpClient c, string url) =>
        Root(await (await c.GetAsync(url)).Content.ReadAsStringAsync()).GetProperty("data")
            .EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();

    [Fact]
    public async Task Own_translatable_filter_resolves_at_the_query_locale()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var stamp = "Tr" + Guid.NewGuid().ToString("N")[..6];
        // "zh-TW" cannot be spelled as a C# anonymous-object property name, so the translations payload
        // is a Dictionary<string, object> here (System.Text.Json serializes both shapes identically;
        // TranslationQueryTests uses the same pattern for the same reason).
        var hit = await Post(c, "article", new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = stamp + "-en" },
                ["zh-TW"] = new { title = stamp + "-zh" },
            }
        });

        (await Ids(c, $"/api/items/article?filter%5Btitle%5D%5B_eq%5D={stamp}-en")).Should().Contain(hit);
        (await Ids(c, $"/api/items/article?filter%5Btitle%5D%5B_eq%5D={stamp}-zh")).Should().NotContain(hit);
        (await Ids(c, $"/api/items/article?locale=zh-TW&filter%5Btitle%5D%5B_eq%5D={stamp}-zh")).Should().Contain(hit);
    }

    // R4: category has no translation sidecar of its own; the translatable leaf lives on articles, one
    // relation hop away. Proves the pushdown works for BOTH the dotted each-exists form
    // ("articles.title") and the `_some` same-row form ("articles._some.title" AND
    // "articles._some.status" bound to the SAME related article) — with a status variant that must
    // NOT match, so the _some binding is proven, not just its title half.
    [Fact]
    public async Task Cross_hop_translatable_leaf_from_a_root_without_sidecar()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var stamp = "Hop" + Guid.NewGuid().ToString("N")[..6];
        var cat = await Post(c, "category", new { name = stamp });
        await Post(c, "article", new { status = "draft", categoryId = cat, translations = new { en = new { title = stamp + "-title" } } });

        (await Ids(c, $"/api/items/category?filter%5Barticles.title%5D%5B_eq%5D={stamp}-title"))
            .Should().Contain(cat, "dotted-path each-exists form: category -> articles.title, resolved via the article_translations sidecar");

        (await Ids(c, $"/api/items/category?filter%5Barticles._some.title%5D%5B_contains%5D={stamp}&filter%5Barticles._some.status%5D%5B_eq%5D=draft"))
            .Should().Contain(cat, "_some binds both conditions to the SAME related article, which is status=draft");

        (await Ids(c, $"/api/items/category?filter%5Barticles._some.title%5D%5B_contains%5D={stamp}&filter%5Barticles._some.status%5D%5B_eq%5D=published"))
            .Should().NotContain(cat, "no related article is both title-matching and status=published");
    }

    [Fact]
    public async Task Translatable_search_still_finds_rows_by_title()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var stamp = "Srch" + Guid.NewGuid().ToString("N")[..6];
        var byTitle = await Post(c, "article", new { status = "draft", translations = new { en = new { title = stamp + "-only-title" } } });

        var resp = await c.GetAsync($"/api/items/article?search={stamp}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Ids(c, $"/api/items/article?search={stamp}")).Should().Contain(byTitle);
    }
}
