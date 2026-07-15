// tests/Struo.Tests/GraphQl/GraphQlRevisionTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Task 8 (Phase 9c): GraphQL parity for revisions — proves the same round trip
/// <see cref="Struo.Tests.Api.RevisionEndpointTests"/> drives over REST also works over
/// <c>/graphql</c> through the real host pipeline (ApiFactory): the shared <c>Revision</c> type,
/// <c>articleRevisions</c>/<c>articleRevision</c> query fields, and the <c>revertArticle</c>
/// mutation. Drives via "article" ([CmsCollection(Revisions = true)] in the sample schema) since
/// "category" is not revisioned and must NOT expose these fields at all.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlRevisionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private static async Task<JsonElement> PostGraphQlAsync(HttpClient client, string query)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Root(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> CreateArticleAsync(HttpClient client, string title, string status = "draft")
    {
        var root = await PostGraphQlAsync(client,
            $"mutation {{ createArticle(input: {{ status: \"{status}\", translations: [ {{ locale: \"en\", fields: {{ title: \"{title}\" }} }} ] }}) {{ id }} }}");
        root.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        return root.GetProperty("data").GetProperty("createArticle").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task ArticleRevisions_lists_and_revertArticle_reapplies()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateArticleAsync(client, "GqlRevisionRoundTrip", status: "draft"); // rev 1

        var update = await PostGraphQlAsync(client,
            $"mutation {{ updateArticle(id: \"{id}\", input: {{ status: \"published\" }}) {{ status }} }}");
        update.TryGetProperty("errors", out var updateErrors).Should().BeFalse($"unexpected errors: {updateErrors}");
        update.GetProperty("data").GetProperty("updateArticle").GetProperty("status").GetString()
            .Should().Be("published"); // rev 2

        var list1 = await PostGraphQlAsync(client,
            $"{{ articleRevisions(id: \"{id}\") {{ revisionNumber operation }} }}");
        var revisions1 = list1.GetProperty("data").GetProperty("articleRevisions");
        revisions1.GetArrayLength().Should().Be(2);
        revisions1[0].GetProperty("operation").GetString().Should().Be("update");
        revisions1[0].GetProperty("revisionNumber").GetInt64().Should().Be(2);
        revisions1[1].GetProperty("operation").GetString().Should().Be("create");
        revisions1[1].GetProperty("revisionNumber").GetInt64().Should().Be(1);

        var revert = await PostGraphQlAsync(client,
            $"mutation {{ revertArticle(id: \"{id}\", revisionNumber: 1) {{ status }} }}");
        revert.TryGetProperty("errors", out var revertErrors).Should().BeFalse($"unexpected errors: {revertErrors}");
        revert.GetProperty("data").GetProperty("revertArticle").GetProperty("status").GetString()
            .Should().Be("draft");

        var list2 = await PostGraphQlAsync(client,
            $"{{ articleRevisions(id: \"{id}\") {{ revisionNumber operation }} }}");
        var revisions2 = list2.GetProperty("data").GetProperty("articleRevisions");
        revisions2.GetArrayLength().Should().Be(3);
        revisions2[0].GetProperty("operation").GetString().Should().Be("revert");
        revisions2[0].GetProperty("revisionNumber").GetInt64().Should().Be(3);
    }

    [Fact]
    public async Task ArticleRevision_returns_snapshot_any()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateArticleAsync(client, "GqlRevisionSnapshot");

        var single = await PostGraphQlAsync(client,
            $"{{ articleRevision(id: \"{id}\", revisionNumber: 1) {{ operation snapshot }} }}");
        single.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        var node = single.GetProperty("data").GetProperty("articleRevision");
        node.GetProperty("operation").GetString().Should().Be("create");
        var snapshot = node.GetProperty("snapshot");
        snapshot.ValueKind.Should().Be(JsonValueKind.Object);
        snapshot.GetProperty("status").GetString().Should().Be("draft");
    }

    /// <summary>SEC-2: <c>xArticleRevision</c> shares <c>ItemService.GetRevisionAsync</c> with the REST
    /// endpoint, so it must return the same redacted snapshot. Article's Hidden fields
    /// (<c>internalNote</c>, <c>internalSlug</c>) are excluded from the GraphQL schema entirely (by
    /// design — <see cref="GraphQlSchemaTests"/>), so they cannot be set via a GraphQL mutation; this
    /// test seeds them over REST (which does not filter Hidden fields on write) on the SAME host/DB the
    /// GraphQL endpoint reads from, then proves the GraphQL read redacts them regardless.</summary>
    [Fact]
    public async Task ArticleRevision_redacts_hidden_fields()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/items/article", new
        {
            status = "draft",
            internalNote = "gql-secret-token",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "GqlHiddenFieldRedaction", internalSlug = "gql-secret-en" },
            }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var single = await PostGraphQlAsync(client,
            $"{{ articleRevision(id: \"{id}\", revisionNumber: 1) {{ operation snapshot }} }}");
        single.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        var snapshotRaw = single.GetProperty("data").GetProperty("articleRevision").GetProperty("snapshot").GetRawText();
        snapshotRaw.Should().NotContain("gql-secret-token");
        snapshotRaw.Should().NotContain("gql-secret-en");
        snapshotRaw.Should().NotContain("internalNote");
        snapshotRaw.Should().NotContain("internalSlug");
    }

    [Fact]
    public async Task RevertArticle_unknown_revision_is_null()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateArticleAsync(client, "GqlRevisionUnknownRevert");

        var result = await PostGraphQlAsync(client,
            $"mutation {{ revertArticle(id: \"{id}\", revisionNumber: 999) {{ id }} }}");
        result.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        result.GetProperty("data").GetProperty("revertArticle").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Category has no [CmsCollection(Revisions=true)] — these fields must not exist at all.</summary>
    [Fact]
    public async Task Non_revisioned_collection_has_no_revision_fields()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(
            "/graphql",
            new { query = "{ __schema { queryType { fields { name } } mutationType { fields { name } } } }" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();

        var queryFields = doc.RootElement.GetProperty("data").GetProperty("__schema").GetProperty("queryType")
            .GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        var mutationFields = doc.RootElement.GetProperty("data").GetProperty("__schema").GetProperty("mutationType")
            .GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();

        queryFields.Should().Contain(["articleRevisions", "articleRevision"]);
        mutationFields.Should().Contain("revertArticle");
        queryFields.Should().NotContain(["categoryRevisions", "categoryRevision"]);
        mutationFields.Should().NotContain("revertCategory");
    }
}
