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

    private static async Task AssertCapRejectionAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        var error = Root(body).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Contain("too many");
    }

    [Fact]
    public async Task Cross_relation_filter_resolving_past_the_cap_is_rejected()
    {
        var stamp = "CapLeaf" + Guid.NewGuid().ToString("N")[..8];
        var admin = await _factory.CreateAuthenticatedClientAsync();
        // Cap + 1 categories matching the stamp -> the leaf id set alone exceeds the cap.
        for (var i = 0; i <= Cap; i++)
            await PostAsync(admin, "category", new { name = $"{stamp}-{i}" });

        var capped = CreateCappedClient();
        var response = await QueryArticlesAsync(capped, new
        {
            filter = new Dictionary<string, object> { ["category.name"] = Contains(stamp) },
        });

        await AssertCapRejectionAsync(response);
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
    public async Task Cross_relation_filter_whose_intermediate_hop_exceeds_the_cap_is_rejected()
    {
        // Distinct code path from the leaf guard: here the LEAF (one parent category) is well within
        // the cap and it is the walk-back hop (its children) that blows past it.
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

        await AssertCapRejectionAsync(response);
    }

    [Fact]
    public async Task Translatable_search_resolving_past_the_cap_is_rejected()
    {
        // article's only Searchable field is the translatable ArticleTranslation.Title, so ?search=
        // goes exclusively through the translation-sidecar parent-id union in SqlSugarItemRepository —
        // a separate materialization site from RelationFilterResolver's.
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

        await AssertCapRejectionAsync(response);
    }
}
