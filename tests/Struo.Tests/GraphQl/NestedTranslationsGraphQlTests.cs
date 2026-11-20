using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

// A nested object's `translations` field lists the locales and values the same row has when it is
// selected as a root.
[Collection("ApiIntegration")]
public class NestedTranslationsGraphQlTests(ApiFactory factory)
{
    private const string Selection = "translations { locale fields }";

    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static async Task<string> Post(HttpClient c, string collection, object body)
    {
        var response = await c.PostAsJsonAsync($"/api/items/{collection}", body);
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return Root(text).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> Gql(HttpClient c, string query)
    {
        var response = await c.PostAsJsonAsync("/graphql", new { query });
        var text = await response.Content.ReadAsStringAsync();
        var root = Root(text);
        root.TryGetProperty("errors", out _).Should().BeFalse(text);
        return root.GetProperty("data");
    }

    private static Dictionary<string, object> Titles(string en, string? zh = null)
    {
        var map = new Dictionary<string, object> { ["en"] = new { title = en } };
        if (zh is not null) map["zh-TW"] = new { title = zh };
        return map;
    }

    private static Dictionary<string, string> Flatten(JsonElement translations) =>
        translations.EnumerateArray().ToDictionary(
            t => t.GetProperty("locale").GetString()!,
            t => t.GetProperty("fields").GetProperty("title").GetString()!);

    [Fact]
    public async Task One_to_many_children_list_their_translations()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "fxCategory", new { name = "GqlNestedCat" });
        var both = await Post(c, "fxArticle", new { status = "draft", categoryId = cat, translations = Titles("GqlEn", "GqlZh") });
        var enOnly = await Post(c, "fxArticle", new { status = "draft", categoryId = cat, translations = Titles("GqlOnlyEn") });

        var single = (await Gql(c, $$"""{ fxCategory(id: "{{cat}}") { articles { id {{Selection}} } } }"""))
            .GetProperty("fxCategory").GetProperty("articles");
        var list = (await Gql(c, $$"""{ fxCategories(filter: { id: { eq: "{{cat}}" } }) { items { articles { id {{Selection}} } } } }"""))
            .GetProperty("fxCategories").GetProperty("items")[0].GetProperty("articles");

        foreach (var articles in new[] { single, list })
        {
            var byId = articles.EnumerateArray()
                .ToDictionary(a => a.GetProperty("id").GetString()!, a => Flatten(a.GetProperty("translations")));
            byId[both].Should().Equal(new Dictionary<string, string> { ["en"] = "GqlEn", ["zh-TW"] = "GqlZh" });
            byId[enOnly].Should().Equal(new Dictionary<string, string> { ["en"] = "GqlOnlyEn" });
        }
    }

    [Fact]
    public async Task Many_to_one_and_many_to_many_targets_list_their_translations()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var g1 = await Post(c, "fxGlossary", new { translations = Titles("G1En", "G1Zh") });
        var g2 = await Post(c, "fxGlossary", new { translations = Titles("G2En") });
        var lex = await Post(c, "fxLexicon", new { name = "GqlLex", glossaryId = g1, glossaries = new[] { g1, g2 } });

        var data = (await Gql(c, $$"""{ fxLexicon(id: "{{lex}}") { glossary { {{Selection}} } glossaries { id {{Selection}} } } }"""))
            .GetProperty("fxLexicon");

        Flatten(data.GetProperty("glossary").GetProperty("translations"))
            .Should().Equal(new Dictionary<string, string> { ["en"] = "G1En", ["zh-TW"] = "G1Zh" });
        var many = data.GetProperty("glossaries").EnumerateArray()
            .ToDictionary(g => g.GetProperty("id").GetString()!, g => Flatten(g.GetProperty("translations")));
        many[g1].Keys.Order().Should().Equal("en", "zh-TW");
        many[g2].Keys.Should().Equal("en");
    }

    [Fact]
    public async Task The_root_locale_argument_filters_the_nested_translations()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "fxCategory", new { name = "GqlLocaleCat" });
        var both = await Post(c, "fxArticle", new { status = "draft", categoryId = cat, translations = Titles("LocEn", "LocZh") });
        var enOnly = await Post(c, "fxArticle", new { status = "draft", categoryId = cat, translations = Titles("LocOnlyEn") });

        var articles = (await Gql(c, $$"""{ fxCategory(id: "{{cat}}", locale: "zh-TW") { articles { id {{Selection}} } } }"""))
            .GetProperty("fxCategory").GetProperty("articles");

        var byId = articles.EnumerateArray()
            .ToDictionary(a => a.GetProperty("id").GetString()!, a => Flatten(a.GetProperty("translations")));
        byId[both].Should().Equal(new Dictionary<string, string> { ["zh-TW"] = "LocZh" });
        byId[enOnly].Should().BeEmpty();
    }
}
