// tests/Struo.Tests/GraphQl/GraphQlLongOverflowTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// A <c>Long</c> value above <c>long.MaxValue</c> or below <c>long.MinValue</c> never reaches a
/// resolver as a server fault: the <c>version</c> input field and revert argument answer
/// <c>INVALID_VERSION</c> (the REST code for the same value) when it is a literal; a variable, whose
/// target the error does not name, and any other <c>Long</c> answer <c>BAD_USER_INPUT</c>. The row stays
/// untouched and the overflow is not logged as an unhandled resolver exception.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlLongOverflowTests(ApiFactory factory)
{
    private const string Over = "9223372036854775808";
    private const string Under = "-9223372036854775809";

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private async Task<(HttpClient Client, string Id)> CreateArticleAsync()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/items/fxArticle",
            new { status = "draft", translations = new { en = new { title = "L" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
        return (client, id);
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, object payload)
    {
        var resp = await client.PostAsJsonAsync("/graphql", payload);
        return Root(await resp.Content.ReadAsStringAsync());
    }

    private static void AssertSingleCode(JsonElement root, string code)
    {
        root.TryGetProperty("errors", out var errors).Should().BeTrue(root.GetRawText());
        errors.GetArrayLength().Should().Be(1, root.GetRawText());
        errors[0].GetProperty("extensions").GetProperty("code").GetString().Should().Be(code, root.GetRawText());
        errors[0].GetProperty("message").GetString().Should().NotContain("Unexpected Execution Error");
    }

    private static async Task AssertRowUntouchedAsync(HttpClient client, string id) =>
        Root(await client.GetStringAsync($"/api/items/fxArticle/{id}"))
            .GetProperty("data").GetProperty("status").GetString().Should().Be("draft");

    [Theory]
    [InlineData(Over)]
    [InlineData(Under)]
    public async Task Update_with_an_overflowing_version_literal_is_INVALID_VERSION(string literal)
    {
        var (client, id) = await CreateArticleAsync();
        var query = $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ status: \"published\", version: {literal} }}) {{ id }} }}";

        AssertSingleCode(await PostAsync(client, new { query }), "INVALID_VERSION");
        await AssertRowUntouchedAsync(client, id);
    }

    [Theory]
    [InlineData(Over)]
    [InlineData(Under)]
    public async Task Update_with_an_overflowing_version_variable_is_BAD_USER_INPUT(string value)
    {
        var (client, id) = await CreateArticleAsync();
        var query = "mutation($v: Long) { updateFxArticle(id: \"" + id + "\", input: { status: \"published\", version: $v }) { id } }";
        var payload = JsonDocument.Parse($"{{\"query\":{JsonSerializer.Serialize(query)},\"variables\":{{\"v\":{value}}}}}")
            .RootElement.Clone();

        AssertSingleCode(await PostAsync(client, payload), "BAD_USER_INPUT");
        await AssertRowUntouchedAsync(client, id);
    }

    [Theory]
    [InlineData(Over)]
    [InlineData(Under)]
    public async Task Revert_with_an_overflowing_version_literal_is_INVALID_VERSION(string literal)
    {
        var (client, id) = await CreateArticleAsync();
        var query = $"mutation {{ revertFxArticle(id: \"{id}\", revisionNumber: 1, version: {literal}) {{ id }} }}";

        AssertSingleCode(await PostAsync(client, new { query }), "INVALID_VERSION");
        await AssertRowUntouchedAsync(client, id);
    }

    [Theory]
    [InlineData(Over)]
    [InlineData(Under)]
    public async Task Revert_with_an_overflowing_version_variable_is_BAD_USER_INPUT(string value)
    {
        var (client, id) = await CreateArticleAsync();
        var query = "mutation($v: Long) { revertFxArticle(id: \"" + id + "\", revisionNumber: 1, version: $v) { id } }";
        var payload = JsonDocument.Parse($"{{\"query\":{JsonSerializer.Serialize(query)},\"variables\":{{\"v\":{value}}}}}")
            .RootElement.Clone();

        AssertSingleCode(await PostAsync(client, payload), "BAD_USER_INPUT");
        await AssertRowUntouchedAsync(client, id);
    }

    [Fact]
    public async Task Update_with_a_fractional_version_literal_is_not_a_server_fault()
    {
        var (client, id) = await CreateArticleAsync();
        var query = $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ status: \"published\", version: 1.5 }}) {{ id }} }}";

        var root = await PostAsync(client, new { query });

        var error = root.GetProperty("errors")[0];
        error.TryGetProperty("extensions", out var extensions).Should().BeTrue(root.GetRawText());
        extensions.TryGetProperty("code", out var code).Should().BeFalse(root.GetRawText());
        error.GetProperty("message").GetString().Should().NotContain("Unexpected Execution Error");
        await AssertRowUntouchedAsync(client, id);
    }
}
