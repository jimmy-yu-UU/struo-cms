using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class DeepNestingValidationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Builds a self-referential category.parent chain nested `depth` levels deep:
    // depth 1 => {"parent":{}} ; depth 2 => {"parent":{"deep":{"parent":{}}}} ; ...
    private static object NestParent(int depth) =>
        depth <= 1
            ? new { parent = new { } }
            : new { parent = new { deep = NestParent(depth - 1) } };

    [Fact]
    public async Task Nested_deep_over_max_depth_returns_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        // ExpandDeepAsync only validates when there's at least one parent row to expand
        // (the entities.Count==0 guard is existing, unchanged behaviour) — seed one category.
        await c.PostAsJsonAsync("/api/items/category", new { name = "DepthSeed" });
        // MaxRelationDepth = 6 => a 7-level nested chain must be rejected before any query runs.
        var envelope = JsonSerializer.SerializeToElement(new { deep = NestParent(7) });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_nested_relation_returns_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        // Seed one article row so the parent query returns entities and validation actually runs.
        await c.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "UnknownNestedSeed" } } });
        var envelope = JsonSerializer.SerializeToElement(new
        {
            deep = new { category = new { deep = new { ghostrel = new { } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Nested_deep_over_max_depth_returns_400_even_with_no_matching_rows()
    {
        // Validity must not depend on result-set size: an over-depth deep request against an
        // empty/no-match collection must still be rejected (400), not silently return 200-empty.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = System.Guid.NewGuid().ToString() } }, // matches nothing
            deep = NestParent(7)
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Many_sibling_relations_at_depth_one_are_allowed()
    {
        // Sibling relations at the same nesting depth do not count toward MaxRelationDepth: article
        // has category + tags (2 siblings) at depth 1, which must return 200.
        var c = await _factory.CreateAuthenticatedClientAsync();
        await c.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "SiblingSeed" } } });
        var envelope = JsonSerializer.SerializeToElement(new
        {
            deep = new { category = new { }, tags = new { } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
