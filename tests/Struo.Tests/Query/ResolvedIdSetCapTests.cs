// tests/Struo.Tests/Query/ResolvedIdSetCapTests.cs
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// A cross-relation (dotted) filter and a translatable-field search are both implemented by
/// resolving the condition to a set of root ids and rewriting it into an own-collection
/// <c>id IN (...)</c>. That intermediate id set was materialized with NO cardinality bound:
/// <c>StruoQueryOptions</c> capped limit/offset/filter-conditions/relation-depth, but nothing capped
/// how many ids a single hop could pull into memory. On a large table a wide condition
/// (<c>?filter[category.name][_contains]=a</c>, <c>?search=a</c>) therefore cost O(table) memory plus
/// a single multi-megabyte SQL statement — reachable by any caller with read access, including an
/// anonymous one wherever <c>public</c> holds a read grant.
/// <para>
/// <c>Query:MaxResolvedFilterIds</c> bounds it. Exceeding the cap is a client error (400
/// <c>BAD_USER_INPUT</c>, "narrow the filter"), never a silent truncation — truncating would return
/// quietly wrong rows, which is worse than refusing.
/// </para>
/// Each test runs on its own derived host with a deliberately tiny cap, and stamps its fixtures so
/// the shared SQLite database's other rows cannot affect the counts.
/// </summary>
[Collection("ApiIntegration")]
public class ResolvedIdSetCapTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private const int Cap = 2;

    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static object Eq(object v) => new Dictionary<string, object> { ["_eq"] = v };
    private static object Contains(object v) => new Dictionary<string, object> { ["_contains"] = v };

    /// <summary>Derived host whose only difference is the tiny resolved-id cap.</summary>
    private HttpClient CreateCappedClient() =>
        _factory
            .WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
                c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Query:MaxResolvedFilterIds"] = Cap.ToString(),
                })))
            .CreateClient();

    private static async Task<string> PostAsync(HttpClient c, string collection, object body)
    {
        var response = await c.PostAsJsonAsync($"/api/items/{collection}", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return Root(await response.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<HttpResponseMessage> QueryArticlesAsync(HttpClient c, object envelope) =>
        await c.PostAsJsonAsync("/api/items/article/query", JsonSerializer.SerializeToElement(envelope));

    [Fact]
    public async Task Cross_relation_filter_resolving_past_the_cap_is_not_refused()
    {
        // Task 5 removed ItemService's redundant materialize-then-rewrite step (relation paths were
        // already pushed down as SQL subqueries by FilterTranslator inside the repository; the
        // ItemService-level rewrite ran first and was the only thing left enforcing this cap for a
        // cross-relation filter). No id set is materialized here anymore, so a wide related set — more
        // than Cap categories matching the stamp — must no longer be refused, mirroring
        // SubqueryPushdownTests.Wide_related_set_beyond_the_old_cap_is_not_refused and this file's own
        // Translatable_search_beyond_the_old_cap_is_not_refused for the same drop of the old cap.
        var stamp = "CapLeaf" + Guid.NewGuid().ToString("N")[..8];
        var admin = await _factory.CreateAuthenticatedClientAsync();
        // Cap + 1 categories matching the stamp -> the related set alone would have exceeded the old cap.
        for (var i = 0; i <= Cap; i++)
            await PostAsync(admin, "category", new { name = $"{stamp}-{i}" });

        var capped = CreateCappedClient();
        var response = await QueryArticlesAsync(capped, new
        {
            filter = new Dictionary<string, object> { ["category.name"] = Contains(stamp) },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        // No article references any of these categories, so the pushed-down query legitimately
        // returns zero rows — the point is the 200, not the row count.
        Root(await response.Content.ReadAsStringAsync()).GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Cross_relation_filter_resolving_within_the_cap_still_works()
    {
        // Control for the test above: the cap must not break a filter that resolves to a small set.
        var stamp = "CapOk" + Guid.NewGuid().ToString("N")[..8];
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var category = await PostAsync(admin, "category", new { name = stamp });
        var article = await PostAsync(admin, "article", new
        {
            status = "draft",
            categoryId = category,
            translations = new { en = new { title = $"{stamp} article" } },
        });

        var capped = CreateCappedClient();
        var response = await QueryArticlesAsync(capped, new
        {
            filter = new Dictionary<string, object> { ["category.name"] = Eq(stamp) },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Root(await response.Content.ReadAsStringAsync()).GetProperty("data")
            .EnumerateArray().Select(r => r.GetProperty("id").GetString())
            .Should().Contain(article);
    }

    [Fact]
    public async Task Cross_relation_filter_whose_intermediate_hop_exceeds_the_cap_is_not_refused()
    {
        // Distinct code path from the leaf case above: here the LEAF (one parent category) is well
        // within the cap and it is the walk-back hop (its children) that would have blown past it
        // under the old materialize-then-rewrite path. Nested as a subquery instead (see Task 5's
        // ItemService change above), it is no longer refused either.
        var stamp = "CapHop" + Guid.NewGuid().ToString("N")[..8];
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var parent = await PostAsync(admin, "category", new { name = stamp });
        for (var i = 0; i <= Cap; i++)
            await PostAsync(admin, "category", new { name = $"{stamp}-child-{i}", parentId = parent });

        var capped = CreateCappedClient();
        var response = await QueryArticlesAsync(capped, new
        {
            filter = new Dictionary<string, object> { ["category.parent.name"] = Eq(stamp) },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Root(await response.Content.ReadAsStringAsync()).GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Translatable_search_beyond_the_old_cap_is_not_refused()
    {
        // article's only Searchable field is the translatable ArticleTranslation.Title. This search
        // used to be answered by materializing a parent-id set from the translation sidecar (capped by
        // Query:MaxResolvedFilterIds, same as a cross-relation filter) — U3 pushed it down to a
        // "<coll>.id IN (SELECT fk FROM translation WHERE locale = ? AND title LIKE ?)" subquery
        // instead (FilterTranslator.TranslatableLeaf), so no id set is ever materialized here anymore.
        // This mirrors SubqueryPushdownTests.Wide_related_set_beyond_the_old_cap_is_not_refused for the
        // relation-filter case: exceeding the old cap must no longer be refused.
        var stamp = "CapSearch" + Guid.NewGuid().ToString("N")[..8];
        var admin = await _factory.CreateAuthenticatedClientAsync();
        for (var i = 0; i <= Cap; i++)
            await PostAsync(admin, "article", new
            {
                status = "draft",
                translations = new { en = new { title = $"{stamp} number {i}" } },
            });

        var capped = CreateCappedClient();
        var response = await QueryArticlesAsync(capped, new { search = stamp });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Root(await response.Content.ReadAsStringAsync()).GetProperty("data").GetArrayLength().Should().Be(Cap + 1);
    }
}
