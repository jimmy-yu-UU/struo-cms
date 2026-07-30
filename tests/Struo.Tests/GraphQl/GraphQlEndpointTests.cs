// tests/Struo.Tests/GraphQl/GraphQlEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Proves /graphql is reachable through the REAL host pipeline (auth/CSRF/permission
/// middleware in front of MapGraphQL, exactly as Program.cs wires it — see
/// GraphQlServiceCollectionExtensions.MapStruoGraphQl) rather than only through the in-process
/// IRequestExecutor used by GraphQlExecutionTests. Also proves the
/// AddMaxExecutionDepthRule(12, ...) guard configured there actually rejects an over-deep query
/// end-to-end.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    /// <summary>
    /// The schema/permission layers (RBAC, per-collection ItemService checks) sit inside collection
    /// field resolvers, not on the endpoint itself, so an anonymous "{ __typename }" request must
    /// clear the whole real pipeline (auth + CSRF + permission middleware, then HotChocolate) and
    /// resolve against the root Query type without any credentials.
    /// </summary>
    [Fact]
    public async Task Typename_query_reaches_resolver_through_real_pipeline()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/graphql", new { query = "{ __typename }" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("data").GetProperty("__typename").GetString().Should().Be("Query");
    }

    /// <summary>
    /// Category is self-referential (parent: Category) in the sample schema, which lets a single
    /// selection set recurse arbitrarily deep without needing 12+ distinct collections. The rule is
    /// registered as AddMaxExecutionDepthRule(12, ...) in GraphQlServiceCollectionExtensions and
    /// runs at document-validation time — before any resolver executes — so the query is rejected
    /// purely on shape; no seeded data or authentication is required for this to fail. Confirmed via
    /// a throwaway diagnostic run that HotChocolate answers this class of validation error with
    /// HTTP 400 (GraphQL-over-HTTP "request error" semantics), unlike execution/field errors which
    /// are HTTP 200 with an "errors" array — hence no StatusCode assertion here, only the error
    /// shape, so this test doesn't depend on that HTTP-status-code convention.
    /// </summary>
    [Fact]
    public async Task Over_depth_query_is_rejected_by_max_execution_depth_rule()
    {
        // categories -> items -> parent{parent{...{name}...}} : 25 nested "parent" selections,
        // comfortably past the configured max of 12 regardless of exactly how the root/list levels
        // are counted.
        const int nestedParents = 25;
        var query = "{ categories { items { " +
                    string.Concat(Enumerable.Repeat("parent { ", nestedParents)) +
                    "name" +
                    string.Concat(Enumerable.Repeat(" }", nestedParents)) +
                    " } } }";

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/graphql", new { query });

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue(
            $"expected the max-execution-depth rule to reject this query; full response: {json}");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        var messages = errors.EnumerateArray().Select(e => e.GetProperty("message").GetString()).ToList();
        messages.Should().Contain(m => m != null && m.Contains("depth", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every mutation-execution test in this suite builds its own in-process
    /// executor with an ".AddMutationType(...)" anchor, so none of them prove that the REAL
    /// production wiring — AddStruoGraphQl in GraphQlServiceCollectionExtensions, which registers
    /// the production AddMutationType anchor plus the mutation type extension — actually exposes
    /// per-collection mutations through the real host. This test closes that gap: it introspects
    /// the live schema's mutation root through the same ApiFactory pipeline used by
    /// Typename_query_reaches_resolver_through_real_pipeline and asserts the discovered sample
    /// "article" [CmsCollection] contributed createArticle/updateArticle/deleteArticle, proving
    /// AddStruoGraphQl actually wires mutations end-to-end rather than only in test-local executors.
    /// </summary>
    [Fact]
    public async Task Mutation_root_exposes_per_collection_mutations_through_real_pipeline()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/graphql",
            new { query = "{ __schema { mutationType { fields { name } } } }" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse(
            $"expected no errors introspecting the mutation root; full response: {json}");

        var fieldNames = doc.RootElement.GetProperty("data").GetProperty("__schema")
            .GetProperty("mutationType").GetProperty("fields")
            .EnumerateArray()
            .Select(f => f.GetProperty("name").GetString())
            .ToList();

        fieldNames.Should().Contain(["createArticle", "updateArticle", "deleteArticle"]);
    }
}
