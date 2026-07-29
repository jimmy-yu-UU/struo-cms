// tests/Struo.Tests/GraphQl/NestedListArgsExecutionTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// GraphQL to-many relation fields (e.g. <c>category { articles(...) }</c>)
/// declared <c>filter</c>/<c>sort</c>/<c>limit</c>/<c>offset</c> arguments in the schema,
/// but <see cref="Struo.Api.GraphQl.CollectionResolvers"/>'s selection-walk (<c>BuildDeep</c>) never
/// read them into the <see cref="Struo.Domain.Query.DeepRelationSpec"/> the query engine consumes —
/// so the arguments were schema-only decoration with no effect. These two tests drive the real
/// /graphql HTTP endpoint (through <see cref="ApiFactory"/>, i.e. real ItemService +
/// RelationExpander behind the resolver, not a fake data source) and prove the nested
/// <c>articles</c> selection's <c>filter</c>+<c>limit</c> actually prune the rows the engine
/// returns, for both argument-passing shapes HotChocolate supports: an inline literal, and a
/// <c>$variable</c> — the latter is the shape that risks landing as an uncoerced/null
/// <c>ArgumentValue.Value</c> on the compiled child selection (see the BuildDeep comment for why).
/// </summary>
[Collection("ApiIntegration")]
public class NestedListArgsExecutionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private static async Task<string> Post(HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    private static async Task<JsonElement> PostGraphQl(HttpClient c, string query, object? variables = null)
    {
        var response = await c.PostAsJsonAsync("/graphql", new { query, variables });
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("errors", out var errors).Should().BeFalse(
            $"expected no GraphQL errors; full response: {json}");
        return doc.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Nested_list_filter_and_limit_via_inline_literal()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "GqlArgsCat-8c3b" });
        await Post(c, "article", new
        {
            status = "published",
            categoryId = cat,
            translations = new { en = new { title = "GA-pub-8c3b" } }
        });
        await Post(c, "article", new
        {
            status = "draft",
            categoryId = cat,
            translations = new { en = new { title = "GA-drf-8c3b" } }
        });

        var query = $$"""
        query { category(id: "{{cat}}") {
            articles(filter: { status: { eq: "published" } }, limit: 5) { status }
        } }
        """;
        var data = await PostGraphQl(c, query);
        var articles = data.GetProperty("category").GetProperty("articles");
        articles.GetArrayLength().Should().Be(1,
            "the draft sibling must be excluded by the nested filter, not just the published one included");
        articles[0].GetProperty("status").GetString().Should().Be("published");
    }

    [Fact]
    public async Task Nested_list_filter_via_variable()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "GqlVarCat-8c3b" });
        await Post(c, "article", new
        {
            status = "published",
            categoryId = cat,
            translations = new { en = new { title = "GV-pub-8c3b" } }
        });
        await Post(c, "article", new
        {
            status = "draft",
            categoryId = cat,
            translations = new { en = new { title = "GV-drf-8c3b" } }
        });

        var query = $$"""
        query($f: ArticleFilterInput) { category(id: "{{cat}}") {
            articles(filter: $f) { status }
        } }
        """;
        var variables = new { f = new { status = new { eq = "published" } } };
        var data = await PostGraphQl(c, query, variables);
        var articles = data.GetProperty("category").GetProperty("articles");
        articles.GetArrayLength().Should().Be(1,
            "the $variable-passed filter must coerce and reach the engine exactly like the inline literal does");
    }
}
