// tests/Struo.Tests/Api/InvalidVersionEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// A <c>version</c> that is present, not JSON null, and not a parseable long fails the whole update with
/// 400 INVALID_VERSION on a collection that carries a version; the row stays untouched. Absent or null
/// means last-write-wins; a numeric string is accepted; a stale value is still 409 VERSION_CONFLICT.
/// A collection without a version ignores the key.
/// </summary>
[Collection("ApiIntegration")]
public class InvalidVersionEndpointTests(ApiFactory factory)
{
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private static StringContent Json(string raw) => new(raw, Encoding.UTF8, "application/json");

    private async Task<(HttpClient Client, string Id, long Version)> CreateArticleAsync()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/items/fxArticle",
            new { status = "draft", translations = new { en = new { title = "V" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = Root(await create.Content.ReadAsStringAsync()).GetProperty("data");
        return (client, data.GetProperty("id").GetString()!, data.GetProperty("version").GetInt64());
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string id) =>
        Root(await client.GetStringAsync($"/api/items/fxArticle/{id}")).GetProperty("data");

    [Theory]
    [InlineData("1.5")]
    [InlineData("3.0")]
    [InlineData("1e3")]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("99999999999999999999")]
    [InlineData("\"\"")]
    public async Task Rest_update_with_an_unparseable_version_is_400_INVALID_VERSION_and_writes_nothing(string literal)
    {
        var (client, id, _) = await CreateArticleAsync();

        var resp = await client.PutAsync($"/api/items/fxArticle/{id}",
            Json($"{{\"status\":\"published\",\"version\":{literal}}}"));

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        Root(body).GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_VERSION");
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("draft");
    }

    [Theory]
    [InlineData("null")]
    [InlineData(null)]
    public async Task Rest_update_with_null_or_absent_version_is_last_write_wins(string? literal)
    {
        var (client, id, _) = await CreateArticleAsync();
        var raw = literal is null ? "{\"status\":\"published\"}" : $"{{\"status\":\"published\",\"version\":{literal}}}";

        var resp = await client.PutAsync($"/api/items/fxArticle/{id}", Json(raw));

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("published");
    }

    [Fact]
    public async Task Rest_update_with_a_numeric_string_version_is_accepted()
    {
        var (client, id, version) = await CreateArticleAsync();

        var resp = await client.PutAsync($"/api/items/fxArticle/{id}",
            Json($"{{\"status\":\"published\",\"version\":\"{version}\"}}"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("published");
    }

    // A version string is an optional sign and digits only: surrounding whitespace is 400, a sign is
    // parsed (and then compared, so a value other than the current one is a 409).
    [Theory]
    [InlineData(" 3")]
    [InlineData("3 ")]
    public async Task Rest_update_with_a_whitespace_padded_version_string_is_400_INVALID_VERSION(string value)
    {
        var (client, id, _) = await CreateArticleAsync();

        var resp = await client.PutAsync($"/api/items/fxArticle/{id}",
            Json($"{{\"status\":\"published\",\"version\":\"{value}\"}}"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, await resp.Content.ReadAsStringAsync());
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("error").GetProperty("code").GetString()
            .Should().Be("INVALID_VERSION");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+3")]
    public async Task Rest_update_with_a_signed_version_string_is_parsed_and_compared(string value)
    {
        var (client, id, _) = await CreateArticleAsync();

        var resp = await client.PutAsync($"/api/items/fxArticle/{id}",
            Json($"{{\"status\":\"published\",\"version\":\"{value}\"}}"));

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict, await resp.Content.ReadAsStringAsync());
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VERSION_CONFLICT");
    }

    [Theory]
    [InlineData("Version")]
    [InlineData("VERSION")]
    public async Task Rest_update_with_an_unparseable_version_under_another_casing_is_400_INVALID_VERSION(string key)
    {
        var (client, id, _) = await CreateArticleAsync();

        var resp = await client.PutAsync($"/api/items/fxArticle/{id}",
            Json($"{{\"status\":\"published\",\"{key}\":\"abc\"}}"));

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        Root(body).GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_VERSION");
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task Rest_update_with_a_stale_version_under_another_casing_is_409_VERSION_CONFLICT()
    {
        var (client, id, version) = await CreateArticleAsync();
        (await client.PutAsJsonAsync($"/api/items/fxArticle/{id}", new { status = "published", version }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await client.PutAsync($"/api/items/fxArticle/{id}",
            Json($"{{\"status\":\"draft\",\"Version\":{version}}}"));

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict, await stale.Content.ReadAsStringAsync());
        Root(await stale.Content.ReadAsStringAsync()).GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VERSION_CONFLICT");
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("published");
    }

    [Theory]
    [InlineData("{\"status\":\"published\",\"version\":@@,\"Version\":@@}")]
    [InlineData("{\"status\":\"published\",\"version\":@@,\"VERSION\":9}")]
    public async Task Rest_update_with_the_version_key_twice_is_400_INVALID_VERSION(string template)
    {
        var (client, id, version) = await CreateArticleAsync();

        var resp = await client.PutAsync($"/api/items/fxArticle/{id}",
            Json(template.Replace("@@", version.ToString())));

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        Root(body).GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_VERSION");
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task Rest_update_with_the_exact_same_version_name_twice_is_400_BAD_USER_INPUT()
    {
        var (client, id, version) = await CreateArticleAsync();

        var resp = await client.PutAsync($"/api/items/fxArticle/{id}",
            Json($"{{\"status\":\"published\",\"version\":{version},\"version\":{version}}}"));

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        Root(body).GetProperty("error").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task Rest_create_ignores_a_version_key()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var resp = await client.PostAsync("/api/items/fxArticle",
            Json("{\"status\":\"draft\",\"version\":\"abc\",\"translations\":{\"en\":{\"title\":\"V\"}}}"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rest_update_on_a_collection_without_a_version_ignores_the_key()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var code = $"v{Guid.NewGuid():N}"[..8];
        var create = await client.PostAsJsonAsync("/api/items/language", new { code, name = "Vtest", sort = 1 });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetRawText().Trim('"');

        var resp = await client.PutAsync($"/api/items/language/{id}", Json("{\"sort\":2,\"version\":\"abc\"}"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        Root(await client.GetStringAsync($"/api/items/language/{id}"))
            .GetProperty("data").GetProperty("sort").GetInt32().Should().Be(2);
    }

    // GraphQL types `version` as Long, so a non-Long literal is rejected by the type layer before the
    // service runs: errors present, no data, row untouched.
    [Theory]
    [InlineData("1.5")]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("99999999999999999999")]
    public async Task GraphQl_update_with_a_non_long_version_is_rejected_and_writes_nothing(string literal)
    {
        var (client, id, _) = await CreateArticleAsync();
        var query = $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ status: \"published\", version: {literal} }}) {{ id }} }}";

        var resp = await client.PostAsJsonAsync("/graphql", new { query });

        var root = Root(await resp.Content.ReadAsStringAsync());
        root.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.GetArrayLength().Should().BeGreaterThan(0);
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task GraphQl_update_with_a_non_long_variable_is_rejected_and_writes_nothing()
    {
        var (client, id, _) = await CreateArticleAsync();
        var query = "mutation($v: Long) { updateFxArticle(id: \"" + id + "\", input: { status: \"published\", version: $v }) { id } }";

        var resp = await client.PostAsJsonAsync("/graphql", new { query, variables = new { v = "abc" } });

        Root(await resp.Content.ReadAsStringAsync()).TryGetProperty("errors", out _).Should().BeTrue();
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task GraphQl_update_with_null_or_absent_version_succeeds_and_a_stale_one_is_VERSION_CONFLICT()
    {
        var (client, id, version) = await CreateArticleAsync();

        foreach (var input in new[] { "status: \"published\"", "status: \"published\", version: null" })
        {
            var resp = await client.PostAsJsonAsync("/graphql",
                new { query = $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ {input} }}) {{ id }} }}" });
            Root(await resp.Content.ReadAsStringAsync()).TryGetProperty("errors", out _).Should().BeFalse();
        }

        var stale = await client.PostAsJsonAsync("/graphql", new
        {
            query = $"mutation {{ updateFxArticle(id: \"{id}\", input: {{ status: \"draft\", version: {version} }}) {{ id }} }}"
        });
        Root(await stale.Content.ReadAsStringAsync()).GetProperty("errors")[0]
            .GetProperty("extensions").GetProperty("code").GetString().Should().Be("VERSION_CONFLICT");
    }
}
