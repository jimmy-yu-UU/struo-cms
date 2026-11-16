// tests/Struo.Tests/Query/LikeLiteralApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// The string operators and <c>search</c> match their value as plain text on every entrance and on every
/// path that shares the translator: REST, <c>POST /query</c>, GraphQL, <c>_or</c>, <c>_and</c>, relation
/// paths, <c>_some</c>, <c>_none</c>, <c>deep</c>, a translated field, a junction payload field, facets
/// and aggregates. Every request is scoped to the rows this class seeds.
/// </summary>
[Collection("ApiIntegration")]
public sealed class LikeLiteralApiTests : IDisposable
{
    private const string Backslash = "\u005c";
    private static readonly string Cjk = "\u4e2d";

    // Category names: the first three also name the articles' categories.
    private static readonly string[] Names =
        ["a_c", "abc", "a%c", "axc", "100%", "1000", "[ab]", "d" + Backslash, Backslash, Cjk];

    private static readonly string[] ArticleCategories = ["a_c", "abc", "a%c"];

    private readonly ApiFactory _factory;
    private readonly Dictionary<string, Guid> _categories = [];
    private readonly Dictionary<string, Guid> _articles = [];
    private readonly Dictionary<string, Guid> _tags = [];
    private HttpClient? _client;

    public LikeLiteralApiTests(ApiFactory factory)
    {
        _factory = factory;
        Seed();
    }

    private ISqlSugarClient Db => _factory.Services.GetRequiredService<ISqlSugarClient>();

    private void Seed()
    {
        foreach (var name in Names)
        {
            var id = Guid.CreateVersion7();
            _categories[name] = id;
            Db.Insertable(new FxCategory { Id = id, Name = name }).ExecuteCommand();
        }
        foreach (var key in ArticleCategories)
        {
            var id = Guid.CreateVersion7();
            _articles[key] = id;
            Db.Insertable(new FxArticle { Id = id, Status = key == "a_c" ? "published" : "draft", Attributes = key, CategoryId = _categories[key] }).ExecuteCommand();
            Db.Insertable(new FxArticleTranslation { FxArticleId = id, Locale = "en", Title = key }).ExecuteCommand();
        }
        foreach (var key in new[] { "a_c", "abc" })
        {
            var id = Guid.CreateVersion7();
            _tags[key] = id;
            Db.Insertable(new FxTag { Id = id, Name = key }).ExecuteCommand();
        }
        Link("a_c", "a_c", "a_c");
        Link("abc", "abc", "abc");
        Link("a%c", "a_c", "abc");
    }

    private void Link(string article, string tag, string note) =>
        Db.Insertable(new FxArticleTag { Id = Guid.CreateVersion7(), ArticleId = _articles[article], TagId = _tags[tag], Note = note, Sort = 1 }).ExecuteCommand();

    public void Dispose()
    {
        var articles = _articles.Values.ToList();
        Db.Deleteable<FxArticleTag>().Where(l => articles.Contains(l.ArticleId)).ExecuteCommand();
        Db.Deleteable<FxArticleTranslation>().Where(t => articles.Contains(t.FxArticleId)).ExecuteCommand();
        Db.Deleteable<FxArticle>().Where(a => articles.Contains(a.Id)).ExecuteCommand();
        var tags = _tags.Values.ToList();
        Db.Deleteable<FxTag>().Where(t => tags.Contains(t.Id)).ExecuteCommand();
        var categories = _categories.Values.ToList();
        Db.Deleteable<FxCategory>().Where(c => categories.Contains(c.Id)).ExecuteCommand();
    }

    // helpers

    private sealed record Reply(HttpStatusCode Status, string Text)
    {
        public JsonElement Root => JsonDocument.Parse(Text).RootElement;
        public int Total => Root.GetProperty("meta").GetProperty("total").GetInt32();
        public List<string> Ids => Root.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetString()!).Order().ToList();
    }

    private async Task<HttpClient> ClientAsync() => _client ??= await _factory.CreateAuthenticatedClientAsync();

    private async Task<Reply> GetAsync(string url)
    {
        var response = await (await ClientAsync()).GetAsync(url);
        return new Reply(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<Reply> PostAsync(string collection, string json, string query = "")
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await (await ClientAsync()).PostAsync($"/api/items/{collection}/query{query}", content);
        return new Reply(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string J(string s) => JsonSerializer.Serialize(s);
    private static string Q(string s) => Uri.EscapeDataString(s);

    private List<string> CategoryIds(IEnumerable<string> names) => names.Select(n => _categories[n].ToString()).Order().ToList();

    private string CategoryScope => string.Join(",", _categories.Values.Select(i => J(i.ToString())));
    private string CategoryScopeCsv => string.Join(",", _categories.Values);
    private string ArticleScope => string.Join(",", _articles.Values.Select(i => J(i.ToString())));
    private string ArticleScopeCsv => string.Join(",", _articles.Values);

    private static bool Matches(string op, string text, string value) => op switch
    {
        "_contains" => text.Contains(value, StringComparison.Ordinal),
        "_starts_with" => text.StartsWith(value, StringComparison.Ordinal),
        _ => text.EndsWith(value, StringComparison.Ordinal),
    };

    private List<string> ExpectedCategories(string op, string value) =>
        CategoryIds(Names.Where(n => Matches(op, n, value)));

    private static void Agrees(Reply r, List<string> expected)
    {
        r.Status.Should().Be(HttpStatusCode.OK, r.Text);
        r.Ids.Should().Equal(expected, r.Text);
        r.Total.Should().Be(expected.Count);
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var op in new[] { "_contains", "_starts_with", "_ends_with" })
            foreach (var value in new[] { "a_c", "%", "_", "a%", "100%", Backslash, "d" + Backslash, "[ab]", "[", Cjk, "ab" })
                yield return [op, value];
    }

    // the three entrances

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task REST_and_POST_match_the_value_as_plain_text_and_total_agrees(string op, string value)
    {
        var expected = ExpectedCategories(op, value);
        var rest = await GetAsync($"/api/items/fxCategory?limit=100&filter[id][_in]={CategoryScopeCsv}&filter[name][{op}]={Q(value)}");
        Agrees(rest, expected);
        var post = await PostAsync("fxCategory", $"{{\"limit\":100,\"filter\":{{\"id\":{{\"_in\":[{CategoryScope}]}},\"name\":{{\"{op}\":{J(value)}}}}}}}");
        Agrees(post, expected);
    }

    [Theory]
    [InlineData("contains", "_contains", "a_c")]
    [InlineData("contains", "_contains", "%")]
    [InlineData("startsWith", "_starts_with", "a%")]
    [InlineData("endsWith", "_ends_with", "%c")]
    [InlineData("endsWith", "_ends_with", "d\u005c")]
    [InlineData("contains", "_contains", "[ab]")]
    public async Task GraphQL_matches_the_value_as_plain_text(string graphqlOp, string op, string value)
    {
        var query = "{ fxCategories(limit: 100, filter: { id: { in: [" + CategoryScope + "] }, name: { " + graphqlOp + ": " + J(value) + " } }) { total items { id } } }";
        var response = await (await ClientAsync()).PostAsJsonAsync("/graphql", new { query });
        var text = await response.Content.ReadAsStringAsync();
        var list = JsonDocument.Parse(text).RootElement.GetProperty("data").GetProperty("fxCategories");
        var expected = ExpectedCategories(op, value);
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).Order().Should().Equal(expected, text);
        list.GetProperty("total").GetInt32().Should().Be(expected.Count);
    }

    [Fact]
    public async Task An_empty_value_matches_every_scoped_row_and_a_trailing_backslash_is_not_an_error()
    {
        var all = ExpectedCategories("_contains", "");
        Agrees(await PostAsync("fxCategory", $"{{\"limit\":100,\"filter\":{{\"id\":{{\"_in\":[{CategoryScope}]}},\"name\":{{\"_contains\":\"\"}}}}}}"), all);
        Agrees(await GetAsync($"/api/items/fxCategory?limit=100&filter[id][_in]={CategoryScopeCsv}&filter[name][_ends_with]={Q("d" + Backslash)}"),
            CategoryIds(["d" + Backslash]));
    }

    // groups

    [Fact]
    public async Task An_or_and_an_and_of_string_leaves_match_each_value_as_plain_text()
    {
        var or = await PostAsync("fxCategory",
            $"{{\"limit\":100,\"filter\":{{\"_or\":[{{\"name\":{{\"_contains\":\"a_c\"}}}},{{\"name\":{{\"_starts_with\":\"100%\"}}}}]}}}}");
        or.Status.Should().Be(HttpStatusCode.OK, or.Text);
        or.Ids.Where(CategoryIds(Names).Contains).Should().Equal(CategoryIds(["a_c", "100%"]), or.Text);
        var and = await PostAsync("fxCategory",
            $"{{\"limit\":100,\"filter\":{{\"_and\":[{{\"id\":{{\"_in\":[{CategoryScope}]}}}},{{\"name\":{{\"_contains\":\"a_\"}}}},{{\"name\":{{\"_ends_with\":\"_c\"}}}}]}}}}");
        Agrees(and, CategoryIds(["a_c"]));
        var viaQuery = await GetAsync($"/api/items/fxCategory?limit=100&filter[id][_in]={CategoryScopeCsv}&filter[name][_contains]=a_&filter[name][_ends_with]=_c");
        Agrees(viaQuery, CategoryIds(["a_c"]));
    }

    // relations

    [Fact]
    public async Task A_relation_path_matches_the_value_as_plain_text()
    {
        var r = await GetAsync($"/api/items/fxArticle?limit=100&filter[id][_in]={ArticleScopeCsv}&filter[category.name][_contains]=a_c");
        Agrees(r, [_articles["a_c"].ToString()]);
        var percent = await GetAsync($"/api/items/fxArticle?limit=100&filter[id][_in]={ArticleScopeCsv}&filter[category.name][_ends_with]={Q("%c")}");
        Agrees(percent, [_articles["a%c"].ToString()]);
    }

    [Fact]
    public async Task Some_and_none_match_the_value_as_plain_text()
    {
        var some = await GetAsync($"/api/items/fxCategory?limit=100&filter[id][_in]={CategoryScopeCsv}&filter[articles._some.attributes][_contains]=a_c");
        Agrees(some, CategoryIds(["a_c"]));
        var scope = string.Join(",", ArticleCategories.Select(n => _categories[n]));
        var none = await GetAsync($"/api/items/fxCategory?limit=100&filter[id][_in]={scope}&filter[articles._none.attributes][_contains]=a_c");
        Agrees(none, CategoryIds(["abc", "a%c"]));
    }

    [Fact]
    public async Task Deep_matches_the_value_as_plain_text()
    {
        var scope = string.Join(",", ArticleCategories.Select(n => J(_categories[n].ToString())));
        var r = await PostAsync("fxCategory",
            $"{{\"limit\":100,\"filter\":{{\"id\":{{\"_in\":[{scope}]}}}},\"deep\":{{\"articles\":{{\"filter\":{{\"attributes\":{{\"_contains\":\"a_c\"}}}}}}}}}}");
        r.Status.Should().Be(HttpStatusCode.OK, r.Text);
        var counts = r.Root.GetProperty("data").EnumerateArray()
            .ToDictionary(c => c.GetProperty("id").GetString()!, c => c.GetProperty("articles").GetArrayLength());
        counts[_categories["a_c"].ToString()].Should().Be(1);
        counts[_categories["abc"].ToString()].Should().Be(0);
        counts[_categories["a%c"].ToString()].Should().Be(0);
    }

    [Fact]
    public async Task A_translated_field_matches_the_value_as_plain_text()
    {
        var rest = await GetAsync($"/api/items/fxArticle?limit=100&locale=en&filter[id][_in]={ArticleScopeCsv}&filter[title][_contains]=a_c");
        Agrees(rest, [_articles["a_c"].ToString()]);
        var post = await PostAsync("fxArticle",
            $"{{\"limit\":100,\"filter\":{{\"id\":{{\"_in\":[{ArticleScope}]}},\"title\":{{\"_starts_with\":\"a%\"}}}}}}", "?locale=en");
        Agrees(post, [_articles["a%c"].ToString()]);
    }

    [Fact]
    public async Task A_junction_payload_field_matches_the_value_as_plain_text()
    {
        var note = await GetAsync($"/api/items/fxArticle?limit=100&filter[id][_in]={ArticleScopeCsv}&filter[tags._some._junction.note][_contains]=a_c");
        Agrees(note, [_articles["a_c"].ToString()]);
        var each = await GetAsync($"/api/items/fxArticle?limit=100&filter[id][_in]={ArticleScopeCsv}&filter[tags._junction.note][_starts_with]=a_");
        Agrees(each, [_articles["a_c"].ToString()]);
        var target = await GetAsync($"/api/items/fxArticle?limit=100&filter[id][_in]={ArticleScopeCsv}&filter[tags._some.name][_contains]=a_c");
        Agrees(target, new[] { _articles["a_c"], _articles["a%c"] }.Select(i => i.ToString()).Order().ToList());
    }

    [Fact]
    public async Task Trashed_rows_are_matched_as_plain_text_too()
    {
        Db.Updateable<FxCategory>().SetColumns(c => c.DeletedAt == DateTime.UtcNow).Where(c => c.Id == _categories["a_c"] || c.Id == _categories["abc"]).ExecuteCommand();
        var only = await GetAsync($"/api/items/fxCategory?limit=100&deleted=only&filter[id][_in]={CategoryScopeCsv}&filter[name][_contains]=a_c");
        Agrees(only, CategoryIds(["a_c"]));
        var with = await GetAsync($"/api/items/fxCategory?limit=100&deleted=with&filter[id][_in]={CategoryScopeCsv}&filter[name][_starts_with]=a_");
        Agrees(with, CategoryIds(["a_c"]));
        var live = await GetAsync($"/api/items/fxCategory?limit=100&filter[id][_in]={CategoryScopeCsv}&filter[name][_contains]=a_c");
        Agrees(live, []);
    }

    // search, facets and aggregates

    [Fact]
    public async Task Search_matches_the_term_as_plain_text()
    {
        var own = await GetAsync($"/api/items/fxCategory?limit=100&filter[id][_in]={CategoryScopeCsv}&search=a_c");
        Agrees(own, CategoryIds(["a_c"]));
        var percent = await GetAsync($"/api/items/fxCategory?limit=100&filter[id][_in]={CategoryScopeCsv}&search={Q("00%")}");
        Agrees(percent, CategoryIds(["100%"]));
        var translated = await GetAsync($"/api/items/fxArticle?limit=100&locale=en&filter[id][_in]={ArticleScopeCsv}&search=a_c");
        Agrees(translated, [_articles["a_c"].ToString()]);
    }

    [Fact]
    public async Task Facets_and_aggregates_use_the_same_plain_text_filter()
    {
        var r = await GetAsync(
            $"/api/items/fxArticle?limit=100&filter[id][_in]={ArticleScopeCsv}&filter[attributes][_contains]=a_c&facets=status,category.name&aggregate[count]=status");
        Agrees(r, [_articles["a_c"].ToString()]);
        var meta = r.Root.GetProperty("meta");
        var status = meta.GetProperty("facets").GetProperty("status").EnumerateArray()
            .Select(b => (b.GetProperty("value").GetString(), b.GetProperty("count").GetInt64())).ToList();
        status.Should().Equal(("published", 1L));
        meta.GetProperty("facets").GetProperty("category.name").GetArrayLength().Should().Be(1);
        meta.GetProperty("aggregate").GetProperty("count").GetProperty("status").GetInt64().Should().Be(1);
    }
}
