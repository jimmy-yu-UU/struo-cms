// tests/Struo.Tests/GraphQl/GraphQlSoftDeleteTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// GraphQL read/write parity for soft delete — proves the same round trip
/// <see cref="Struo.Tests.Api.SoftDeleteEndpointTests"/> drives over REST also works over
/// <c>/graphql</c> through the real host pipeline (ApiFactory): the <c>deleted</c> list-query
/// argument, <c>deleteX(purge)</c>, and <c>restoreX</c>. Drives via "category" (no translation
/// sidecar, so create needs no locale-gated fields) rather than "article".
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlSoftDeleteTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private static async Task<JsonElement> PostGraphQlAsync(HttpClient client, string query)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Root(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> CreateCategoryAsync(HttpClient client, string name)
    {
        var root = await PostGraphQlAsync(client,
            $"mutation {{ createCategory(input: {{ name: \"{name}\" }}) {{ id }} }}");
        root.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        return root.GetProperty("data").GetProperty("createCategory").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Query_excludes_deleted_and_ONLY_shows_them()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateCategoryAsync(client, "GqlSoftDeleteExcludeOnly");

        var deleted = await PostGraphQlAsync(client, $"mutation {{ deleteCategory(id: \"{id}\") }}");
        deleted.GetProperty("data").GetProperty("deleteCategory").GetBoolean().Should().BeTrue();

        // Default (deleted: EXCLUDE) omits the soft-deleted row.
        var excluded = await PostGraphQlAsync(client,
            $"{{ categories(filter: {{ id: {{ eq: \"{id}\" }} }}) {{ items {{ id }} total }} }}");
        excluded.GetProperty("data").GetProperty("categories").GetProperty("total").GetInt32().Should().Be(0);

        // deleted: ONLY surfaces it.
        var only = await PostGraphQlAsync(client,
            $"{{ categories(filter: {{ id: {{ eq: \"{id}\" }} }}, deleted: ONLY) {{ items {{ id }} total }} }}");
        only.GetProperty("data").GetProperty("categories").GetProperty("total").GetInt32().Should().Be(1);
        only.GetProperty("data").GetProperty("categories").GetProperty("items")[0]
            .GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task DeleteX_soft_deletes_and_restoreX_reverts()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateCategoryAsync(client, "GqlSoftDeleteRestoreRoundTrip");

        var deleted = await PostGraphQlAsync(client, $"mutation {{ deleteCategory(id: \"{id}\") }}");
        deleted.GetProperty("data").GetProperty("deleteCategory").GetBoolean().Should().BeTrue();

        var absent = await PostGraphQlAsync(client,
            $"{{ categories(filter: {{ id: {{ eq: \"{id}\" }} }}) {{ total }} }}");
        absent.GetProperty("data").GetProperty("categories").GetProperty("total").GetInt32().Should().Be(0);

        var restored = await PostGraphQlAsync(client, $"mutation {{ restoreCategory(id: \"{id}\") {{ id }} }}");
        restored.TryGetProperty("errors", out var restoreErrors).Should().BeFalse($"unexpected errors: {restoreErrors}");
        restored.GetProperty("data").GetProperty("restoreCategory").GetProperty("id").GetString().Should().Be(id);

        var reappeared = await PostGraphQlAsync(client,
            $"{{ categories(filter: {{ id: {{ eq: \"{id}\" }} }}) {{ total }} }}");
        reappeared.GetProperty("data").GetProperty("categories").GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task DeleteX_purge_removes_permanently()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateCategoryAsync(client, "GqlSoftDeletePurge");

        var deleted = await PostGraphQlAsync(client, $"mutation {{ deleteCategory(id: \"{id}\") }}");
        deleted.GetProperty("data").GetProperty("deleteCategory").GetBoolean().Should().BeTrue();

        var purged = await PostGraphQlAsync(client, $"mutation {{ deleteCategory(id: \"{id}\", purge: true) }}");
        purged.GetProperty("data").GetProperty("deleteCategory").GetBoolean().Should().BeTrue();

        // deleted: WITH — the row is gone even ignoring the soft-delete floor.
        var withDeleted = await PostGraphQlAsync(client,
            $"{{ categories(filter: {{ id: {{ eq: \"{id}\" }} }}, deleted: WITH) {{ total }} }}");
        withDeleted.GetProperty("data").GetProperty("categories").GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Deleted_ONLY_without_delete_permission_is_FORBIDDEN()
    {
        var (client, _) = await _factory.CreateEditorClientAsync(
            readCollections: ["category"], writeCollections: []);

        var response = await client.PostAsJsonAsync("/graphql",
            new { query = "{ categories(deleted: ONLY) { total } }" });
        response.StatusCode.Should().Be(HttpStatusCode.OK); // execution error, not a request-validation error
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("FORBIDDEN");
    }
}
