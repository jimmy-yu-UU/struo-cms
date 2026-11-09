// tests/Struo.Tests/GraphQl/GraphQlNumberCoercionTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Tests.Support;
using Xunit;
using FileEntity = Struo.Infrastructure.Files.File;

namespace Struo.Tests.GraphQl;

/// <summary>
/// An Int, Long or Float input the schema cannot coerce is a client error with a stable code: a literal
/// or variable that is out of range or of the wrong kind answers <c>BAD_USER_INPUT</c>, except a
/// literal <c>version</c> (input field or <c>revert</c> argument), which answers <c>INVALID_VERSION</c>
/// as on REST. Nothing is written and the failure is not an unhandled resolver exception. A <c>long</c>
/// field filters through <c>LongFilter</c>, so a value above 2^31 filters instead of failing.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlNumberCoercionTests(ApiFactory factory)
{
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private async Task<(HttpClient Client, string Id)> CreateArticleAsync()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/items/fxArticle",
            new { status = "draft", translations = new { en = new { title = "N" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
        return (client, id);
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, object payload)
    {
        var resp = await client.PostAsJsonAsync("/graphql", payload);
        return Root(await resp.Content.ReadAsStringAsync());
    }

    private static object WithVariable(string query, string rawJsonValue) =>
        JsonDocument.Parse($"{{\"query\":{JsonSerializer.Serialize(query)},\"variables\":{{\"v\":{rawJsonValue}}}}}")
            .RootElement.Clone();

    private static void AssertSingleCode(JsonElement root, string code)
    {
        root.TryGetProperty("errors", out var errors).Should().BeTrue(root.GetRawText());
        errors.GetArrayLength().Should().Be(1, root.GetRawText());
        errors[0].GetProperty("extensions").GetProperty("code").GetString().Should().Be(code, root.GetRawText());
        var message = errors[0].GetProperty("message").GetString();
        message.Should().NotContain("Unexpected Execution Error").And.NotContain("internal error");
        message.Should().NotContain("coerce").And.NotContain("does not match");
    }

    private static async Task AssertRowUntouchedAsync(HttpClient client, string id) =>
        Root(await client.GetStringAsync($"/api/items/fxArticle/{id}"))
            .GetProperty("data").GetProperty("status").GetString().Should().Be("draft");

    [Theory]
    [InlineData("limit: 99999999999")]
    [InlineData("offset: 99999999999")]
    [InlineData("limit: -99999999999")]
    [InlineData("limit: \"abc\"")]
    [InlineData("offset: true")]
    [InlineData("limit: 1.5")]
    public async Task A_list_argument_literal_that_is_not_an_Int_is_BAD_USER_INPUT(string argument)
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        AssertSingleCode(await PostAsync(client, new { query = $"{{ fxArticles({argument}) {{ total }} }}" }),
            "BAD_USER_INPUT");
    }

    [Theory]
    [InlineData("99999999999")]
    [InlineData("-99999999999")]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    [InlineData("1.5")]
    public async Task A_list_argument_variable_that_is_not_an_Int_is_BAD_USER_INPUT(string value)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var query = "query($v: Int) { fxArticles(limit: $v) { total } }";

        AssertSingleCode(await PostAsync(client, WithVariable(query, value)), "BAD_USER_INPUT");
    }

    [Theory]
    [InlineData("99999999999")]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    [InlineData("1.5")]
    public async Task Revert_with_a_revisionNumber_literal_that_is_not_an_Int_is_BAD_USER_INPUT(string literal)
    {
        var (client, id) = await CreateArticleAsync();
        var query = $"mutation {{ revertFxArticle(id: \"{id}\", revisionNumber: {literal}) {{ id }} }}";

        AssertSingleCode(await PostAsync(client, new { query }), "BAD_USER_INPUT");
        await AssertRowUntouchedAsync(client, id);
    }

    [Theory]
    [InlineData("99999999999")]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    [InlineData("1.5")]
    public async Task Revert_with_a_revisionNumber_variable_that_is_not_an_Int_is_BAD_USER_INPUT(string value)
    {
        var (client, id) = await CreateArticleAsync();
        var query = "mutation($v: Int!) { revertFxArticle(id: \"" + id + "\", revisionNumber: $v) { id } }";

        AssertSingleCode(await PostAsync(client, WithVariable(query, value)), "BAD_USER_INPUT");
        await AssertRowUntouchedAsync(client, id);
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    [InlineData("1.5")]
    public async Task Update_with_a_version_literal_of_the_wrong_kind_is_INVALID_VERSION(string literal)
    {
        var (client, id) = await CreateArticleAsync();
        var query = $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ status: \"published\", version: {literal} }}) {{ id }} }}";

        AssertSingleCode(await PostAsync(client, new { query }), "INVALID_VERSION");
        await AssertRowUntouchedAsync(client, id);
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    [InlineData("1.5")]
    public async Task Revert_with_a_version_literal_of_the_wrong_kind_is_INVALID_VERSION(string literal)
    {
        var (client, id) = await CreateArticleAsync();
        var query = $"mutation {{ revertFxArticle(id: \"{id}\", revisionNumber: 1, version: {literal}) {{ id }} }}";

        AssertSingleCode(await PostAsync(client, new { query }), "INVALID_VERSION");
        await AssertRowUntouchedAsync(client, id);
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    [InlineData("1.5")]
    public async Task Update_with_a_version_variable_of_the_wrong_kind_is_BAD_USER_INPUT(string value)
    {
        var (client, id) = await CreateArticleAsync();
        var query = "mutation($v: Long) { updateFxArticle(id: \"" + id + "\", input: { status: \"published\", version: $v }) { id } }";

        AssertSingleCode(await PostAsync(client, WithVariable(query, value)), "BAD_USER_INPUT");
        await AssertRowUntouchedAsync(client, id);
    }

    [Fact]
    public async Task A_valid_Int_and_Long_still_succeed()
    {
        var (client, id) = await CreateArticleAsync();

        var list = await PostAsync(client, new { query = "{ fxArticles(limit: 1, offset: 0) { total } }" });
        list.TryGetProperty("errors", out _).Should().BeFalse(list.GetRawText());

        var version = Root(await client.GetStringAsync($"/api/items/fxArticle/{id}"))
            .GetProperty("data").GetProperty("version").GetInt64();
        var update = await PostAsync(client, new
        {
            query = $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ status: \"published\", version: {version} }}) {{ id }} }}"
        });
        update.TryGetProperty("errors", out _).Should().BeFalse(update.GetRawText());
    }

    private const long Large = 3_000_000_000L;

    private async Task<(HttpClient Client, Guid Big, Guid Small)> SeedFilesAsync()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        async Task<Guid> UploadAsync(string name)
        {
            var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(name));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
            var up = await client.PostAsync("/api/files", new MultipartFormDataContent { { content, "file", name + ".txt" } });
            up.StatusCode.Should().Be(HttpStatusCode.Created, await up.Content.ReadAsStringAsync());
            return Root(await up.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetGuid();
        }

        var big = await UploadAsync($"big-{Guid.NewGuid():N}");
        var small = await UploadAsync($"small-{Guid.NewGuid():N}");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Updateable<FileEntity>().SetColumns(f => f.Size == Large + 5).Where(f => f.Id == big).ExecuteCommandAsync();
        return (client, big, small);
    }

    private static async Task<List<Guid>> FileIdsAsync(HttpClient client, string filter)
    {
        var root = await PostAsync(client, new { query = $"{{ files(limit: 100, filter: {filter}) {{ items {{ id }} }} }}" });
        root.TryGetProperty("errors", out _).Should().BeFalse(root.GetRawText());
        return root.GetProperty("data").GetProperty("files").GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!)).ToList();
    }

    [Fact]
    public async Task A_long_field_filters_with_a_value_above_the_Int_range()
    {
        var (client, big, small) = await SeedFilesAsync();

        var gt = await FileIdsAsync(client, $"{{ size: {{ gt: {Large} }} }}");
        gt.Should().Contain(big).And.NotContain(small);

        var eq = await FileIdsAsync(client, $"{{ size: {{ eq: {Large + 5} }} }}");
        eq.Should().Contain(big).And.NotContain(small);

        var inList = await FileIdsAsync(client, $"{{ size: {{ in: [{Large + 5}] }} }}");
        inList.Should().Contain(big).And.NotContain(small);

        var lt = await FileIdsAsync(client, $"{{ size: {{ lt: {Large} }} }}");
        lt.Should().Contain(small).And.NotContain(big);
    }

    private static async Task<List<Guid>> RestFileIdsAsync(HttpClient client, string filter)
    {
        var resp = await client.GetAsync($"/api/items/file?limit=100&{filter}");
        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return Root(body).GetProperty("data").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!)).ToList();
    }

    // REST parity for the LongFilter test above: the same rows come back for the same operands.
    [Fact]
    public async Task Rest_filters_a_long_field_with_a_value_above_the_Int_range()
    {
        var (client, big, small) = await SeedFilesAsync();

        (await RestFileIdsAsync(client, $"filter[size][_gt]={Large}")).Should().Contain(big).And.NotContain(small);
        (await RestFileIdsAsync(client, $"filter[size][_eq]={Large + 5}")).Should().Contain(big).And.NotContain(small);
        (await RestFileIdsAsync(client, $"filter[size][_lt]={Large}")).Should().Contain(small).And.NotContain(big);
    }

    // REST does not refuse a limit it cannot parse as a 32-bit integer: it reads it as missing and
    // pages with the configured default, where GraphQL answers BAD_USER_INPUT for the same value.
    [Fact]
    public async Task Rest_limit_beyond_the_Int_range_falls_back_to_the_default_limit()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var defaultLimit = factory.Services.GetRequiredService<IOptions<StruoQueryOptions>>().Value.DefaultLimit;

        var resp = await client.GetAsync("/api/items/fxArticle?limit=99999999999");

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, body.Length > 300 ? body[..300] : body);
        Root(body).GetProperty("meta").GetProperty("limit").GetInt32().Should().Be(defaultLimit);
    }

    [Theory]
    [InlineData("width: { in: [\"a\"] }")]
    [InlineData("width: { nin: [true] }")]
    [InlineData("width: { in: [1.5] }")]
    [InlineData("size: { in: [\"a\"] }")]
    [InlineData("size: { nin: [true] }")]
    public async Task A_list_operand_of_the_wrong_kind_is_BAD_USER_INPUT(string operand)
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var root = await PostAsync(client, new { query = $"{{ files(filter: {{ {operand} }}) {{ total }} }}" });

        AssertSingleCode(root, "BAD_USER_INPUT");
    }

    [Fact]
    public async Task An_int_field_still_filters_through_IntFilter()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var root = await PostAsync(client, new { query = "{ files(filter: { width: { gt: 99999999999 } }) { total } }" });

        AssertSingleCode(root, "BAD_USER_INPUT");
    }
}
