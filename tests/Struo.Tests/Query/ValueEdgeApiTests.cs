// tests/Struo.Tests/Query/ValueEdgeApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Localization;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// The edges of a filter value on every entrance (REST GET, POST /query, GraphQL) and on every path that
/// shares the translator: relation paths, <c>_some</c>, <c>deep</c>, the translatable leaf and <c>_or</c>.
/// </summary>
[Collection("ApiIntegration")]
public sealed class ValueEdgeApiTests : IDisposable
{
    private const string Marker = "987654";
    private const string LocaleNullTitle = "in-the-null-locale";
    private static readonly string[] SeedCodes = ["null", "[null]", "plain-edge"];

    private readonly ApiFactory _factory;
    private readonly Guid _categoryId = Guid.CreateVersion7();
    private readonly Guid _otherCategoryId = Guid.CreateVersion7();
    private readonly Guid _otherArticleId = Guid.CreateVersion7();
    private readonly List<Guid> _articleIds = [];

    public ValueEdgeApiTests(ApiFactory factory)
    {
        _factory = factory;
        Seed();
    }

    private ISqlSugarClient Db => _factory.Services.GetRequiredService<ISqlSugarClient>();

    private void Seed()
    {
        var now = DateTime.UtcNow;
        Cleanup();
        foreach (var code in SeedCodes)
            Db.Insertable(new Language { Code = code, Name = "edge-" + code, CreatedAt = now, UpdatedAt = now, Sort = 3 }).ExecuteCommand();
        Db.Insertable(new FxCategory { Id = _categoryId, Name = "null" }).ExecuteCommand();
        // A second category whose only article has a SQL NULL attribute: it must not match the word null.
        Db.Insertable(new FxCategory { Id = _otherCategoryId, Name = "other" }).ExecuteCommand();
        Db.Insertable(new FxArticle { Id = _otherArticleId, Status = "draft", Attributes = null, CategoryId = _otherCategoryId }).ExecuteCommand();
        // attributes: the text null, the text [null], plain text, SQL NULL.
        string?[] attributes = ["null", "[null]", "plain-edge", null];
        foreach (var attribute in attributes)
        {
            var id = Guid.CreateVersion7();
            _articleIds.Add(id);
            var meta = _articleIds.Count == 1 ? new Dictionary<string, string> { ["edge"] = "marker" } : new Dictionary<string, string>();
            Db.Insertable(new FxArticle { Id = id, Status = "draft", Attributes = attribute, CategoryId = _categoryId, Meta = meta }).ExecuteCommand();
            Db.Insertable(new FxArticleTranslation { FxArticleId = id, Locale = "en", Title = attribute ?? "sql-null" }).ExecuteCommand();
        }
        Db.Insertable(new FxArticleTranslation { FxArticleId = _articleIds[0], Locale = "null", Title = LocaleNullTitle }).ExecuteCommand();
    }

    private void Cleanup()
    {
        var codes = SeedCodes.ToList();
        var articles = _articleIds.Append(_otherArticleId).ToList();
        var categories = new List<Guid> { _categoryId, _otherCategoryId };
        Db.Deleteable<Language>().Where(l => codes.Contains(l.Code)).ExecuteCommand();
        Db.Deleteable<FxArticleTranslation>().Where(t => articles.Contains(t.FxArticleId)).ExecuteCommand();
        Db.Deleteable<FxArticle>().Where(a => articles.Contains(a.Id)).ExecuteCommand();
        Db.Deleteable<FxCategory>().Where(c => categories.Contains(c.Id)).ExecuteCommand();
    }

    public void Dispose() => Cleanup();

    // ── helpers ──────────────────────────────────────────────────────────

    private sealed record Reply(HttpStatusCode Status, string Text)
    {
        public JsonElement Root => JsonDocument.Parse(Text).RootElement;
        public int Rows => Root.GetProperty("data").GetArrayLength();
        public int Total => Root.GetProperty("meta").GetProperty("total").GetInt32();
    }

    private async Task<Reply> GetAsync(string url)
    {
        var response = await _factory.CreateClient().GetAsync(url);
        return new Reply(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<Reply> PostAsync(string collection, string json, string query = "")
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _factory.CreateClient().PostAsync($"/api/items/{collection}/query{query}", content);
        return new Reply(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<Reply> GraphqlAsync(string query)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/graphql", new { query });
        return new Reply(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string J(string s) => JsonSerializer.Serialize(s);

    private static string Filter(string field, string op, string valueJson) =>
        "{\"filter\":{" + J(field) + ":{" + J(op) + ":" + valueJson + "}}}";

    private static string DeepArticles(string field, string op, string valueJson) =>
        "{\"deep\":{\"articles\":{\"filter\":{" + J(field) + ":{" + J(op) + ":" + valueJson + "}}}}}";

    private static string Q(string s) => Uri.EscapeDataString(s);

    private static List<string> Ids(Reply r) =>
        r.Root.GetProperty("data").EnumerateArray().Select(row => row.GetProperty("id").ToString()).ToList();

    private static List<string?> Codes(Reply r) =>
        r.Root.GetProperty("data").EnumerateArray().Select(row => row.GetProperty("code").GetString()).ToList();

    private static List<string?> GraphqlCodes(Reply r) =>
        r.Root.GetProperty("data").GetProperty("languages").GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("code").GetString()).ToList();

    private static void IsBadInput(Reply r, string field, string op)
    {
        r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
        r.Text.Should().Contain("BAD_USER_INPUT").And.Contain($"'{field}'").And.Contain($"'{op}'").And.NotContain(Marker);
    }

    // ── _eq with the words null and [null] ───────────────────────────────

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    public async Task Eq_compares_the_word_as_text_on_every_entrance_and_count_matches_the_page(string word)
    {
        var rest = await GetAsync($"/api/items/language?filter[code][_eq]={Q(word)}");
        var post = await PostAsync("language", Filter("code", "_eq", J(word)));
        foreach (var r in new[] { rest, post })
        {
            r.Status.Should().Be(HttpStatusCode.OK, r.Text);
            Codes(r).Should().Equal(word);
            r.Total.Should().Be(r.Rows);
        }

        var gql = await GraphqlAsync("{ languages(filter: { code: { eq: " + J(word) + " } }) { total items { code } } }");
        gql.Status.Should().Be(HttpStatusCode.OK, gql.Text);
        GraphqlCodes(gql).Should().Equal(word);
        gql.Root.GetProperty("data").GetProperty("languages").GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Eq_with_the_word_null_selects_the_row_that_holds_the_text_and_not_a_sql_null_row()
    {
        var holdsNull = _articleIds[0].ToString();
        var holdsBracketNull = _articleIds[1].ToString();

        var r = await GetAsync("/api/items/fxArticle?limit=100&filter[attributes][_eq]=null");
        r.Status.Should().Be(HttpStatusCode.OK, r.Text);
        Ids(r).Should().Equal(holdsNull);
        r.Total.Should().Be(1);

        var viaPost = await PostAsync("fxArticle", """{"filter":{"attributes":{"_eq":"[null]"}}}""");
        Ids(viaPost).Should().Equal(holdsBracketNull);
        viaPost.Total.Should().Be(1);
    }

    [Fact]
    public async Task Eq_with_the_word_null_works_on_a_relation_path_and_in_an_or_group()
    {
        var relation = await GetAsync("/api/items/fxArticle?limit=100&filter[category.name][_eq]=null");
        relation.Status.Should().Be(HttpStatusCode.OK, relation.Text);
        Ids(relation).Should().BeEquivalentTo(_articleIds.Select(id => id.ToString()));

        // The category that holds the article with the text null, not the one whose article is SQL NULL.
        var some = await GetAsync("/api/items/fxCategory?limit=100&filter[articles._some.attributes][_eq]=null");
        some.Status.Should().Be(HttpStatusCode.OK, some.Text);
        Ids(some).Should().Equal(_categoryId.ToString());

        var or = await PostAsync("language", """{"filter":{"_or":[{"code":{"_eq":"null"}},{"code":{"_eq":"[null]"}}]}}""");
        or.Status.Should().Be(HttpStatusCode.OK, or.Text);
        Codes(or).Should().BeEquivalentTo(["null", "[null]"]);
        or.Total.Should().Be(2);
    }

    // ── the three 500s ───────────────────────────────────────────────────

    [Theory]
    [InlineData("null", 0)]
    [InlineData("[null]", 1)]
    public async Task A_translatable_field_compares_the_word_as_text(string word, int holder)
    {
        var expected = _articleIds[holder].ToString();
        var r = await GetAsync($"/api/items/fxArticle?limit=100&locale=en&filter[title][_eq]={Q(word)}");
        r.Status.Should().Be(HttpStatusCode.OK, r.Text);
        Ids(r).Should().Equal(expected);
        r.Total.Should().Be(1);
        var post = await PostAsync("fxArticle", Filter("title", "_eq", J(word)), "?locale=en");
        post.Status.Should().Be(HttpStatusCode.OK, post.Text);
        Ids(post).Should().Equal(expected);
    }

    [Fact]
    public async Task A_deep_filter_compares_the_word_as_text()
    {
        var r = await PostAsync("fxCategory",
            """{"filter":{"id":{"_eq":"%ID%"}},"deep":{"articles":{"filter":{"attributes":{"_eq":"null"}}}}}""".Replace("%ID%", _categoryId.ToString()));
        r.Status.Should().Be(HttpStatusCode.OK, r.Text);
        r.Root.GetProperty("data")[0].GetProperty("articles").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task The_locale_parameter_null_is_refused_like_any_other_locale_that_does_not_exist()
    {
        Db.Deleteable<Language>().Where(l => l.Code == "null").ExecuteCommand();
        var other = await GetAsync("/api/items/fxArticle?limit=100&locale=zz");
        other.Status.Should().Be(HttpStatusCode.BadRequest, other.Text);
        foreach (var suffix in new[] { "", "&filter[title][_eq]=null", "&filter[title][_contains]=n" })
        {
            var word = await GetAsync("/api/items/fxArticle?limit=100&locale=null" + suffix);
            word.Status.Should().Be(HttpStatusCode.BadRequest, word.Text);
            word.Text.Should().Contain("BAD_USER_INPUT").And.Contain("Unknown or disabled locale 'null'");
        }
        var post = await PostAsync("fxArticle", Filter("title", "_eq", J("null")), "?locale=null");
        post.Status.Should().Be(HttpStatusCode.BadRequest, post.Text);
    }

    [Fact]
    public async Task A_locale_that_is_named_null_is_an_ordinary_locale_whose_code_is_compared_as_text()
    {
        var plain = await GetAsync("/api/items/fxArticle?limit=100&locale=null");
        plain.Status.Should().Be(HttpStatusCode.OK, plain.Text);
        var filtered = await GetAsync($"/api/items/fxArticle?limit=100&locale=null&filter[title][_eq]={LocaleNullTitle}");
        filtered.Status.Should().Be(HttpStatusCode.OK, filtered.Text);
        Ids(filtered).Should().Equal(_articleIds[0].ToString());
        filtered.Total.Should().Be(1);
        var otherLocale = await GetAsync($"/api/items/fxArticle?limit=100&locale=en&filter[title][_eq]={LocaleNullTitle}");
        otherLocale.Rows.Should().Be(0);
    }

    // ── string operators on a field that is not text ─────────────────────

    [Theory]
    [InlineData("sort", "_contains")]
    [InlineData("sort", "_starts_with")]
    [InlineData("enabled", "_ends_with")]
    [InlineData("id", "_contains")]
    [InlineData("createdAt", "_starts_with")]
    [InlineData("createdBy", "_contains")]
    public async Task A_string_operator_on_a_field_that_is_not_text_is_a_400_on_rest_and_post(string field, string op)
    {
        var rest = await GetAsync($"/api/items/language?filter[{field}][{op}]={Marker}");
        IsBadInput(rest, field, op);
        rest.Text.Should().Contain("_eq");

        var post = await PostAsync("language", Filter(field, op, J(Marker)));
        IsBadInput(post, field, op);
    }

    [Fact]
    public async Task A_string_operator_on_a_field_that_is_not_text_is_a_400_on_every_shared_path()
    {
        IsBadInput(await GetAsync($"/api/items/fxArticle?filter[category.id][_contains]={Marker}"), "id", "_contains");
        IsBadInput(await GetAsync($"/api/items/fxCategory?filter[articles._some.publishedAt][_starts_with]={Marker}"), "publishedAt", "_starts_with");
        IsBadInput(await PostAsync("fxCategory",
            DeepArticles("publishedAt", "_contains", J(Marker))), "publishedAt", "_contains");
        IsBadInput(await PostAsync("language",
            "{\"filter\":{\"_or\":[{\"code\":{\"_eq\":\"x\"}},{\"sort\":{\"_contains\":\"" + Marker + "\"}}]}}"), "sort", "_contains");
        IsBadInput(await PostAsync("language",
            "{\"filter\":{\"_and\":[{\"code\":{\"_eq\":\"x\"}},{\"createdAt\":{\"_ends_with\":\"" + Marker + "\"}}]}}"), "createdAt", "_ends_with");
    }

    [Fact]
    public async Task A_string_operator_matches_text_and_JSON_stored_fields()
    {
        var code = await GetAsync("/api/items/language?filter[code][_contains]=ull");
        code.Status.Should().Be(HttpStatusCode.OK, code.Text);
        Codes(code).Should().Contain("null");
        var json = await GetAsync("/api/items/fxArticle?limit=100&filter[attributes][_starts_with]=plain");
        json.Status.Should().Be(HttpStatusCode.OK, json.Text);
        json.Rows.Should().Be(1);
        var list = await GetAsync("/api/items/fxArticle?limit=100&filter[regions][_contains]=apac");
        list.Status.Should().Be(HttpStatusCode.OK, list.Text);
        var title = await GetAsync("/api/items/fxArticle?limit=100&locale=en&filter[title][_contains]=plain");
        title.Status.Should().Be(HttpStatusCode.OK, title.Text);
        title.Rows.Should().Be(1);
    }

    [Theory]
    [InlineData("_starts_with", "{\"edge")]
    [InlineData("_ends_with", "marker\"}")]
    [InlineData("_contains", "edge")]
    public async Task A_string_operator_matches_the_stored_text_of_a_key_value_field(string op, string value)
    {
        var r = await GetAsync($"/api/items/fxArticle?limit=100&filter[meta][{op}]={Q(value)}");
        r.Status.Should().Be(HttpStatusCode.OK, r.Text);
        Ids(r).Should().Equal(_articleIds[0].ToString());
    }

    [Theory]
    [InlineData("""{ languages(filter: { createdBy: { contains: "987654" } }) { items { id } } }""", "createdBy")]
    [InlineData("""{ languages(filter: { createdBy: { startsWith: "987654" } }) { items { id } } }""", "createdBy")]
    public async Task Graphql_answers_the_same_400_message_for_a_string_operator_on_a_Guid_field(string query, string field)
    {
        var r = await GraphqlAsync(query);
        r.Text.Should().Contain("\"errors\"").And.Contain("BAD_USER_INPUT").And.Contain($"'{field}'")
            .And.NotContain("internal error").And.NotContain(Marker);
    }

    // ── null values ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("code", "_eq")]
    [InlineData("code", "_neq")]
    [InlineData("code", "_contains")]
    [InlineData("code", "_in")]
    [InlineData("code", "_nin")]
    [InlineData("sort", "_eq")]
    [InlineData("sort", "_gt")]
    [InlineData("createdBy", "_eq")]
    [InlineData("createdAt", "_neq")]
    public async Task A_null_value_is_a_400_that_points_to_null_and_nnull(string field, string op)
    {
        var r = await PostAsync("language", Filter(field, op, "null"));
        IsBadInput(r, field, op);
        r.Text.Should().Contain("_null").And.Contain("_nnull");
    }

    [Fact]
    public async Task A_null_value_is_a_400_on_every_shared_path()
    {
        IsBadInput(await PostAsync("language", """{"filter":{"_or":[{"code":{"_eq":null}},{"sort":{"_eq":1}}]}}"""), "code", "_eq");
        IsBadInput(await PostAsync("language", """{"filter":{"_or":[{"code":{"_in":null}},{"sort":{"_eq":1}}]}}"""), "code", "_in");
        IsBadInput(await PostAsync("fxArticle", """{"filter":{"category.name":{"_eq":null}}}"""), "name", "_eq");
        IsBadInput(await PostAsync("fxCategory", """{"deep":{"articles":{"filter":{"attributes":{"_eq":null}}}}}"""), "attributes", "_eq");
        IsBadInput(await PostAsync("fxArticle", """{"filter":{"title":{"_eq":null}}}""", "?locale=en"), "title", "_eq");
    }

    [Fact]
    public async Task The_null_operators_test_for_null_on_every_entrance()
    {
        var mine = $"filter[categoryId][_eq]={_categoryId}";
        var rest = await GetAsync($"/api/items/fxArticle?limit=100&{mine}&filter[attributes][_null]=true");
        rest.Status.Should().Be(HttpStatusCode.OK, rest.Text);
        Ids(rest).Should().Equal(_articleIds[3].ToString());
        var notNull = await PostAsync("fxArticle",
            "{\"filter\":{\"categoryId\":{\"_eq\":\"" + _categoryId + "\"},\"attributes\":{\"_nnull\":true}}}");
        Ids(notNull).Should().BeEquivalentTo(_articleIds.Take(3).Select(id => id.ToString()));
        var gqlNull = await GraphqlAsync("""{ fxArticles(limit: 100, filter: { publishedAt: { isNull: true } }) { items { id } } }""");
        gqlNull.Text.Should().NotContain("\"errors\"");
        gqlNull.Root.GetProperty("data").GetProperty("fxArticles").GetProperty("items").GetArrayLength()
            .Should().BeGreaterThanOrEqualTo(_articleIds.Count);
    }

    [Fact]
    public async Task Graphql_skips_a_null_operator_key()
    {
        var all = await GraphqlAsync("""{ languages(limit: 100) { total items { code } } }""");
        var skipped = await GraphqlAsync("""{ languages(limit: 100, filter: { code: { eq: null } }) { total items { code } } }""");
        skipped.Text.Should().NotContain("\"errors\"");
        GraphqlCodes(skipped).Should().HaveCount(GraphqlCodes(all).Count);
    }

    // ── lists for a single-value operator ────────────────────────────────

    [Theory]
    [InlineData("code", "_eq")]
    [InlineData("code", "_neq")]
    [InlineData("code", "_contains")]
    [InlineData("code", "_starts_with")]
    [InlineData("code", "_gt")]
    [InlineData("sort", "_eq")]
    [InlineData("sort", "_lt")]
    [InlineData("createdBy", "_eq")]
    public async Task A_list_for_a_single_value_operator_is_a_400(string field, string op)
    {
        var r = await PostAsync("language", Filter(field, op, "[\"a\",\"b" + Marker + "\"]"));
        IsBadInput(r, field, op);
        r.Text.Should().Contain("single value");
    }

    [Fact]
    public async Task A_list_is_the_value_of_a_set_operator()
    {
        var r = await PostAsync("language", """{"filter":{"code":{"_in":["null","[null]"]}}}""");
        r.Status.Should().Be(HttpStatusCode.OK, r.Text);
        Codes(r).Should().BeEquivalentTo(["null", "[null]"]);
    }

    // ── JSON objects ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("code", "_eq", """{"a":987654}""")]
    [InlineData("code", "_neq", """{"a":987654}""")]
    [InlineData("code", "_contains", """{"a":987654}""")]
    [InlineData("code", "_in", """{"a":987654}""")]
    [InlineData("code", "_nin", """{"a":987654,"b":2}""")]
    [InlineData("code", "_in", """[{"a":987654}]""")]
    [InlineData("code", "_nin", """["x",{"a":987654}]""")]
    [InlineData("code", "_eq", """[{"a":987654}]""")]
    [InlineData("code", "_in", """{}""")]
    [InlineData("sort", "_eq", """{"a":987654}""")]
    [InlineData("sort", "_in", """[{"a":987654}]""")]
    [InlineData("enabled", "_eq", """{"a":987654}""")]
    [InlineData("createdBy", "_in", """{"a":987654}""")]
    public async Task A_JSON_object_as_a_value_or_an_element_is_a_400_for_every_operator(string field, string op, string value)
    {
        var r = await PostAsync("language", Filter(field, op, value));
        IsBadInput(r, field, op);
        r.Text.Should().Contain("JSON object");
    }

    [Fact]
    public async Task A_JSON_object_is_a_400_on_every_shared_path()
    {
        IsBadInput(await PostAsync("language",
            """{"filter":{"_or":[{"code":{"_in":[{"a":987654}]}},{"sort":{"_eq":1}}]}}"""), "code", "_in");
        IsBadInput(await PostAsync("fxArticle", """{"filter":{"category.name":{"_in":{"a":987654}}}}"""), "category.name", "_in");
        IsBadInput(await PostAsync("fxCategory",
            """{"deep":{"articles":{"filter":{"attributes":{"_in":{"a":987654}}}}}}"""), "attributes", "_in");
        IsBadInput(await PostAsync("fxArticle", """{"filter":{"title":{"_in":{"a":987654}}}}""", "?locale=en"), "title", "_in");
        IsBadInput(await PostAsync("fxCategory",
            """{"filter":{"articles":{"_some":{"attributes":{"_eq":{"a":987654}}}}}}"""), "attributes", "_eq");
    }

    [Theory]
    [InlineData("filter[code][_in]=%7B%22a%22%3A1%7D")]
    [InlineData("filter[code][_in]=%7B%22a%22%3A1%2C%22b%22%3A2%7D")]
    [InlineData("filter[code][_eq]=%7B%22a%22%3A1%7D")]
    public async Task The_query_string_reads_a_string_as_text_or_a_comma_list(string query)
    {
        var r = await GetAsync("/api/items/language?" + query);
        r.Status.Should().Be(HttpStatusCode.OK, r.Text);
        r.Rows.Should().Be(0);
    }
}
