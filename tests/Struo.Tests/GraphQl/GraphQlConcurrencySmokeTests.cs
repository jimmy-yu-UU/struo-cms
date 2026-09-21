// tests/Struo.Tests/GraphQl/GraphQlConcurrencySmokeTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Regression guard: GraphQlServiceCollectionExtensions pins both root scopes to
/// DependencyInjectionScope.Request, so a single query selecting multiple root fields
/// (articles/categories/tags) can have HotChocolate execute those sibling resolvers in parallel
/// against the SAME request-scoped ISqlSugarClient. SqlSugarClientFactory.Create returns a
/// SqlSugarScope, SqlSugar's thread-safe wrapper, rather than a bare `new SqlSugarClient(config)`:
/// a bare client is not thread-safe, so concurrent ADO operations on the shared connection would
/// intermittently throw ("connection already open") or otherwise fail.
///
/// This test fires a batch of concurrent multi-root requests and asserts every one comes back
/// 200 with no "errors" array. Per the task plan this is kept as a regression smoke rather than
/// the RED evidence: SQLite (file-per-test, IsAutoCloseConnection) does not reliably reproduce the
/// interleaved-connection race the way a real concurrent Postgres connection pool does, so this
/// test may not reliably FAIL on this provider if the factory regresses — the live Postgres gate is
/// the authoritative RED/GREEN evidence. Kept here as a permanent regression guard for
/// SqlSugarClientFactory.Create's thread-safe return type.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlConcurrencySmokeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private const string MultiRootQuery =
        "{ articles { items { id } } categories { items { id } } tags { items { id } } }";

    [Fact]
    public async Task Concurrent_multi_root_queries_all_succeed_with_no_errors()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        const int concurrency = 20;
        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => client.PostAsJsonAsync("/graphql", new { query = MultiRootQuery }))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        var failures = new List<string>();
        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.OK)
            {
                failures.Add($"status={response.StatusCode}: {body}");
                continue;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out _))
            {
                failures.Add($"errors present: {body}");
            }
        }

        failures.Should().BeEmpty(
            $"expected all {concurrency} concurrent multi-root GraphQL requests to succeed cleanly; failures:\n" +
            string.Join("\n", failures));
    }
}
