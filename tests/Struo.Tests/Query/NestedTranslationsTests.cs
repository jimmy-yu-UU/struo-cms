using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Security.QueryValueInjection;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// An object a `deep` expansion brings in carries `translations` exactly as the same row read as a
// root does, at every level and for every relation kind.
[Collection("ApiIntegration")]
public class NestedTranslationsTests(ApiFactory factory)
{
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static async Task<string> Post(HttpClient c, string collection, object body)
    {
        var response = await c.PostAsJsonAsync($"/api/items/{collection}", body);
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return Root(text).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return Root(text).GetProperty("data");
    }

    private static async Task<JsonElement> Get(HttpClient c, string url) => await Data(await c.GetAsync(url));

    private static async Task<JsonElement> Query(HttpClient c, string collection, object body) =>
        await Data(await c.PostAsJsonAsync($"/api/items/{collection}/query", body));

    private static object IdEquals(string id) => new { id = new Dictionary<string, object> { ["_eq"] = id } };

    private static Dictionary<string, object> Titles(string en, string? zh = null)
    {
        var map = new Dictionary<string, object> { ["en"] = new { title = en } };
        if (zh is not null) map["zh-TW"] = new { title = zh };
        return map;
    }

    private sealed record Seed(string Category, string Both, string EnOnly, string Glossary, string GlossaryEnOnly, string Lexicon);

    private async Task<(HttpClient Client, Seed Ids)> SeedAsync()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "fxCategory", new { name = "NestedCat" });
        var both = await Post(c, "fxArticle", new
        {
            status = "draft", categoryId = cat,
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "BothEn", internalSlug = "secret-en", facts = new { k = "v", n = 1 } },
                ["zh-TW"] = new { title = "BothZh", internalSlug = "secret-zh" },
            },
        });
        var enOnly = await Post(c, "fxArticle", new { status = "draft", categoryId = cat, translations = Titles("EnOnly") });
        var g1 = await Post(c, "fxGlossary", new { translations = Titles("G1En", "G1Zh") });
        var g2 = await Post(c, "fxGlossary", new { translations = Titles("G2En") });
        var lex = await Post(c, "fxLexicon", new { name = "NestedLex", glossaryId = g1, glossaries = new[] { g1, g2 } });
        return (c, new Seed(cat, both, enOnly, g1, g2, lex));
    }

    private static JsonElement ById(JsonElement rows, string id) =>
        rows.EnumerateArray().Single(a => a.GetProperty("id").GetString() == id);

    private static List<string> Locales(JsonElement row) =>
        row.GetProperty("translations").EnumerateObject().Select(p => p.Name).Order().ToList();

    private static string Title(JsonElement row, string locale) =>
        row.GetProperty("translations").GetProperty(locale).GetProperty("title").GetString()!;

    [Fact]
    public async Task One_to_many_children_carry_translations_in_single_list_and_query()
    {
        var (c, s) = await SeedAsync();

        var single = (await Get(c, $"/api/items/fxCategory/{s.Category}?deep=articles")).GetProperty("articles");
        var list = (await Get(c, $"/api/items/fxCategory?filter[id][_eq]={s.Category}&deep=articles"))[0].GetProperty("articles");
        var post = (await Query(c, "fxCategory", new { filter = IdEquals(s.Category), deep = new { articles = new { } } }))[0]
            .GetProperty("articles");

        foreach (var articles in new[] { single, list, post })
        {
            Locales(ById(articles, s.Both)).Should().Equal("en", "zh-TW");
            Title(ById(articles, s.Both), "zh-TW").Should().Be("BothZh");
            Locales(ById(articles, s.EnOnly)).Should().Equal("en");
        }
    }

    [Fact]
    public async Task Nested_translations_equal_the_same_row_read_as_root()
    {
        var (c, s) = await SeedAsync();
        var rootRow = await Get(c, $"/api/items/fxArticle/{s.Both}");
        var nested = ById((await Get(c, $"/api/items/fxCategory/{s.Category}?deep=articles")).GetProperty("articles"), s.Both);

        nested.GetProperty("translations").GetRawText().Should().Be(rootRow.GetProperty("translations").GetRawText());
        nested.GetProperty("translations").GetProperty("en").GetProperty("facts").GetProperty("k").GetString().Should().Be("v");
    }

    [Fact]
    public async Task Locale_filters_nested_translations_strictly()
    {
        var (c, s) = await SeedAsync();
        var articles = (await Get(c, $"/api/items/fxCategory/{s.Category}?deep=articles&locale=zh-TW")).GetProperty("articles");

        Locales(ById(articles, s.Both)).Should().Equal("zh-TW");
        ById(articles, s.EnOnly).GetProperty("translations").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task Hidden_translatable_field_is_absent_from_nested_translations()
    {
        var (c, s) = await SeedAsync();
        var both = ById((await Get(c, $"/api/items/fxCategory/{s.Category}?deep=articles")).GetProperty("articles"), s.Both);

        foreach (var locale in new[] { "en", "zh-TW" })
            both.GetProperty("translations").GetProperty(locale).TryGetProperty("internalSlug", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Nested_fields_do_not_trim_translations()
    {
        var (c, s) = await SeedAsync();
        var articles = (await Query(c, "fxCategory", new
        {
            filter = IdEquals(s.Category),
            deep = new { articles = new { fields = new[] { "id" } } },
        }))[0].GetProperty("articles");

        ById(articles, s.Both).TryGetProperty("status", out _).Should().BeFalse();
        Locales(ById(articles, s.Both)).Should().Equal("en", "zh-TW");
    }

    [Fact]
    public async Task Many_to_one_target_carries_translations()
    {
        var (c, s) = await SeedAsync();
        var lexicon = await Get(c, $"/api/items/fxLexicon/{s.Lexicon}?deep=glossary");

        Locales(lexicon.GetProperty("glossary")).Should().Equal("en", "zh-TW");
        Title(lexicon.GetProperty("glossary"), "zh-TW").Should().Be("G1Zh");
        lexicon.TryGetProperty("translations", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Many_to_many_targets_carry_translations_and_locale_filters_them()
    {
        var (c, s) = await SeedAsync();
        var all = (await Get(c, $"/api/items/fxLexicon/{s.Lexicon}?deep=glossaries")).GetProperty("glossaries");
        Locales(ById(all, s.Glossary)).Should().Equal("en", "zh-TW");
        Locales(ById(all, s.GlossaryEnOnly)).Should().Equal("en");

        var zh = (await Get(c, $"/api/items/fxLexicon/{s.Lexicon}?deep=glossaries&locale=zh-TW")).GetProperty("glossaries");
        Locales(ById(zh, s.Glossary)).Should().Equal("zh-TW");
        ById(zh, s.GlossaryEnOnly).GetProperty("translations").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task Every_level_of_a_nested_deep_carries_translations()
    {
        var (c, s) = await SeedAsync();
        var article = (await Query(c, "fxArticle", new
        {
            filter = IdEquals(s.Both),
            deep = new { category = new { deep = new { articles = new { } } } },
        }))[0];

        Locales(article).Should().Equal("en", "zh-TW");
        var inner = article.GetProperty("category").GetProperty("articles");
        Locales(ById(inner, s.Both)).Should().Equal("en", "zh-TW");
        Locales(ById(inner, s.EnOnly)).Should().Equal("en");
    }

    [Fact]
    public async Task Target_without_a_sidecar_gets_no_translations_key()
    {
        var (c, s) = await SeedAsync();
        var article = await Get(c, $"/api/items/fxArticle/{s.Both}?deep=category");

        article.GetProperty("category").TryGetProperty("translations", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Translations_are_loaded_once_per_level_and_target_regardless_of_row_count()
    {
        var (c, s) = await SeedAsync();
        using var capture = SqlCapture.Attach(factory.Services);

        async Task<int> ArticleTranslationQueries(string url)
        {
            capture.Clear();
            await Get(c, url);
            return capture.Matching("fx_article_translations").Count;
        }

        var fewRows = await ArticleTranslationQueries($"/api/items/fxCategory/{s.Category}?deep=articles");
        for (var i = 0; i < 6; i++)
            await Post(c, "fxArticle", new { status = "draft", categoryId = s.Category, translations = Titles($"More{i}") });
        var manyRows = await ArticleTranslationQueries($"/api/items/fxCategory/{s.Category}?deep=articles");

        fewRows.Should().Be(1);
        manyRows.Should().Be(1);
    }

    [Fact]
    public async Task A_nested_level_loads_translations_in_the_same_number_of_queries_for_any_row_count()
    {
        var (c, s) = await SeedAsync();
        using var capture = SqlCapture.Attach(factory.Services);
        var deep = new { category = new { deep = new { articles = new { } } } };

        async Task<int> ArticleTranslationQueries(object filter)
        {
            capture.Clear();
            var rows = await Query(c, "fxArticle", new { filter, deep });
            rows.GetArrayLength().Should().BeGreaterThan(0);
            return capture.Matching("fx_article_translations").Count;
        }

        var oneParent = await ArticleTranslationQueries(IdEquals(s.Both));

        var categories = new List<string> { s.Category };
        for (var i = 0; i < 3; i++)
        {
            var category = await Post(c, "fxCategory", new { name = $"ManyCat{i}" });
            categories.Add(category);
            for (var j = 0; j < 3; j++)
                await Post(c, "fxArticle", new { status = "draft", categoryId = category, translations = Titles($"Many{i}{j}") });
        }
        var manyParents = await ArticleTranslationQueries(
            new { categoryId = new Dictionary<string, object> { ["_in"] = categories } });

        manyParents.Should().Be(oneParent);
    }

    [Fact]
    public async Task Two_relations_to_one_target_share_one_translation_query()
    {
        var (c, s) = await SeedAsync();
        using var capture = SqlCapture.Attach(factory.Services);

        capture.Clear();
        await Get(c, $"/api/items/fxLexicon?filter[id][_eq]={s.Lexicon}&deep=glossary,glossaries");

        capture.Matching("fx_glossary_translations").Should().HaveCount(1);
    }

    [Fact]
    public async Task Root_translations_stay_one_query_and_a_target_without_a_sidecar_adds_none()
    {
        var (c, s) = await SeedAsync();
        using var capture = SqlCapture.Attach(factory.Services);

        capture.Clear();
        await Get(c, $"/api/items/fxArticle?filter[id][_eq]={s.Both}");
        capture.Matching("fx_article_translations").Should().HaveCount(1);

        capture.Clear();
        await Get(c, $"/api/items/fxArticle?filter[id][_eq]={s.Both}&deep=category");
        capture.Matching("fx_article_translations").Should().HaveCount(1);
    }

    [Fact]
    public async Task A_target_the_caller_cannot_read_is_pruned_without_a_translation_query()
    {
        var (_, s) = await SeedAsync();
        var (blind, _) = await factory.CreateEditorClientAsync(["fxLexicon"], []);
        var (sighted, _) = await factory.CreateEditorClientAsync(["fxLexicon", "fxGlossary"], []);
        using var capture = SqlCapture.Attach(factory.Services);

        capture.Clear();
        var pruned = await Get(blind, $"/api/items/fxLexicon/{s.Lexicon}?deep=glossary,glossaries");
        var prunedQueries = capture.Matching("fx_glossary_translations").Count;
        var visible = await Get(sighted, $"/api/items/fxLexicon/{s.Lexicon}?deep=glossary,glossaries");

        pruned.TryGetProperty("glossary", out _).Should().BeFalse();
        pruned.TryGetProperty("glossaries", out _).Should().BeFalse();
        prunedQueries.Should().Be(0);
        Locales(visible.GetProperty("glossary")).Should().Equal("en", "zh-TW");
        Locales(ById(visible.GetProperty("glossaries"), s.GlossaryEnOnly)).Should().Equal("en");
    }
}
