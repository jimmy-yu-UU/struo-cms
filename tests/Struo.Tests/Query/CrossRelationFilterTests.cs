using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// NOTE: Filter_m2m_tags_name_exists was deleted (Tag entity removed) and
// restored below as Filter_m2m_articles_by_tag_name (de-risk).

[Collection("ApiIntegration")]
public class CrossRelationFilterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static object Eq(object v) => new Dictionary<string, object> { ["_eq"] = v };

    private async Task<string> Post(System.Net.Http.HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    [Fact]
    public async Task Filter_to_one_category_name()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var news = await Post(c, "category", new { name = "FilterNews" });
        var other = await Post(c, "category", new { name = "FilterOther" });
        var hit = await Post(c, "article", new { status = "draft", categoryId = news, translations = new { en = new { title = "HIT" } } });
        var miss = await Post(c, "article", new { status = "draft", categoryId = other, translations = new { en = new { title = "MISS" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.name"] = Eq("FilterNews") }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        ids.Should().Contain(hit);
        ids.Should().NotContain(miss);
    }

    [Fact]
    public async Task Filter_empty_match_returns_zero_rows()
    {
        var c = _factory.CreateClient();
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.name"] = Eq("NoSuchCategoryXYZ") }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Filter_multi_level_category_parent_name()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var parent = await Post(c, "category", new { name = "ParentCat" });
        var child = await Post(c, "category", new { name = "ChildCat", parentId = parent });
        var hit = await Post(c, "article", new { status = "draft", categoryId = child, translations = new { en = new { title = "DEEPHIT" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.parent.name"] = Eq("ParentCat") }
        });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
        data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).Should().Contain(hit);
    }

    // Distinct code path from the two tests above: here the LEAF (one parent category) is narrow and
    // it is the walk-back hop (its children) that is wide. Moved from the retired resolved-id-set-cap
    // test suite — the two-hop dotted path is answered as a correlated SQL subquery rather than a
    // materialized, cardinality-bounded id set, so a parent with many children must not be refused.
    [Fact]
    public async Task Filter_multi_level_category_parent_name_with_a_wide_sibling_set_is_not_refused()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var stamp = "WideHop" + Guid.NewGuid().ToString("N")[..8];
        var parent = await Post(c, "category", new { name = stamp });
        const int childCount = 3;
        for (var i = 0; i < childCount; i++)
            await Post(c, "category", new { name = $"{stamp}-child-{i}", parentId = parent });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.parent.name"] = Eq(stamp) }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        // No article references any of these categories, so the pushed-down query legitimately
        // returns zero rows — the point is the 200, not the row count.
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Filter_relation_path_or_scalar_composes()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "OrCat" });
        var byCat = await Post(c, "article", new { status = "draft", categoryId = cat, translations = new { en = new { title = "ZZZ" } } });
        var byId = await Post(c, "article", new { status = "draft", translations = new { en = new { title = "OrIdUnique" } } });

        // _or over a relation-path condition and a scalar condition
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object>
            {
                ["_or"] = new object[]
                {
                    new Dictionary<string, object> { ["category.name"] = Eq("OrCat") },
                    new Dictionary<string, object> { ["id"] = Eq(byId) }
                }
            }
        });
        var ids = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync())
            .GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        ids.Should().Contain(byCat).And.Contain(byId);
    }

    [Fact]
    public async Task Filter_o2m_category_by_article_id()
    {
        // NOTE: filtering an o2m relation by the child's translatable `title` is locale-aware
        // relation querying. Here we filter the o2m relation by the child's
        // non-translatable own-collection field (id) to exercise the same two-phase resolution.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "O2MFilterCat" });
        var childArticle = await Post(c, "article", new { status = "draft", categoryId = cat, translations = new { en = new { title = "UniqueChildTitle" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["articles.id"] = Eq(childArticle) }
        });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/category/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
        data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).Should().Contain(cat);
    }

    [Fact]
    public async Task Filter_empty_relation_under_or_keeps_scalar_match()
    {
        // _or: relation branch matches nothing, scalar branch matches one article.
        // The empty relation branch must contribute zero ids — not swallow the whole OR.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var hit = await Post(c, "article", new { status = "draft", translations = new { en = new { title = "OrEmptyUnique" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object>
            {
                ["_or"] = new object[]
                {
                    new Dictionary<string, object> { ["category.name"] = Eq("NoSuchCat_or") },
                    new Dictionary<string, object> { ["id"] = Eq(hit) }
                }
            }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var ids = Root(await resp.Content.ReadAsStringAsync())
            .GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("id").GetString()).ToList();
        ids.Should().Contain(hit);
    }

    [Fact]
    public async Task Filter_empty_relation_under_and_returns_zero()
    {
        // _and: relation branch matches nothing, so the whole AND must return 0 rows
        // even though the scalar branch would match.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var art = await Post(c, "article", new { status = "draft", translations = new { en = new { title = "AndEmptyUnique" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object>
            {
                ["_and"] = new object[]
                {
                    new Dictionary<string, object> { ["category.name"] = Eq("NoSuchCat_and") },
                    new Dictionary<string, object> { ["id"] = Eq(art) }
                }
            }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Filter_m2m_articles_by_tag_name()
    {
        // M2M cross-relation filter coverage, restored now that Tag exists again.
        // Proves FilterTranslator's M2M subquery pushdown (junction targetFk -> parentFk) end-to-end
        // on SQLite: "articles that have AT LEAST ONE tag named X" (ANY/EXISTS).
        var c = await _factory.CreateAuthenticatedClientAsync();
        var tag = await Post(c, "tag", new { name = "M2MFilterTag" });
        var tagged = await Post(c, "article", new
        {
            status = "draft",
            tags = new[] { tag },
            translations = new { en = new { title = "TAGGED" } }
        });
        var untagged = await Post(c, "article", new
        {
            status = "draft",
            translations = new { en = new { title = "UNTAGGED" } }
        });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["tags.name"] = Eq("M2MFilterTag") }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var ids = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")
            .EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        ids.Should().Contain(tagged);
        ids.Should().NotContain(untagged);
    }

    private static async Task<List<string?>> IdsAsync(System.Net.Http.HttpClient c, string url) =>
        Root(await (await c.GetAsync(url)).Content.ReadAsStringAsync()).GetProperty("data")
            .EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();

    [Fact]
    public async Task Some_predicate_via_query_string_and_envelope_binds_to_one_link()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var stamp = "Some" + Guid.NewGuid().ToString("N")[..6];
        var guide = await Post(c, "tag", new { name = stamp + "-guide" });
        var misc = await Post(c, "tag", new { name = stamp + "-misc" });
        // hit: ONE link carrying name=guide AND note=hero; miss: guide/plain + misc/hero (cross-row only)
        var hit = await Post(c, "article", new { status = "draft", translations = new { en = new { title = "SomeHit" } },
            tags = new object[] { new { id = guide, note = "hero" } } });
        var miss = await Post(c, "article", new { status = "draft", translations = new { en = new { title = "SomeMiss" } },
            tags = new object[] { new { id = guide, note = "plain" }, new { id = misc, note = "hero" } } });

        var viaQs = await IdsAsync(c, $"/api/items/article?filter%5Btags._some.name%5D%5B_eq%5D={stamp}-guide&filter%5Btags._some._junction.note%5D%5B_eq%5D=hero");
        viaQs.Should().Contain(hit).And.NotContain(miss);

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object>
            {
                ["tags"] = new Dictionary<string, object>
                {
                    ["_some"] = new Dictionary<string, object> { ["name"] = Eq(stamp + "-guide"), ["_junction.note"] = Eq("hero") }
                }
            }
        });
        var viaEnv = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync())
            .GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        viaEnv.Should().Contain(hit).And.NotContain(miss);

        var each = await IdsAsync(c, $"/api/items/article?filter%5Btags.name%5D%5B_eq%5D={stamp}-guide&filter%5Btags._junction.note%5D%5B_eq%5D=hero");
        each.Should().Contain(hit).And.Contain(miss);
    }

    [Fact]
    public async Task None_predicate_includes_articles_without_tags()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var stamp = "None" + Guid.NewGuid().ToString("N")[..6];
        var tag = await Post(c, "tag", new { name = stamp });
        var tagged = await Post(c, "article", new { status = "draft", translations = new { en = new { title = "T" } }, tags = new[] { tag } });
        var bare = await Post(c, "article", new { status = "draft", translations = new { en = new { title = stamp } } });
        var ids = await IdsAsync(c, $"/api/items/article?filter%5Btags._none.name%5D%5B_eq%5D={stamp}");
        ids.Should().Contain(bare).And.NotContain(tagged);
    }

    // Investigation (docs implementer's observation): does a relation filter combine with `search`
    // as AND, or does `search` silently widen the result to every search-matching row regardless of
    // the filter? Both articles satisfy the relation filter (same stamped category); only one's title
    // matches the search term.
    [Fact]
    public async Task Relation_filter_and_search_combine_as_and()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var stamp = "FS" + Guid.NewGuid().ToString("N")[..8];
        var cat = await Post(c, "category", new { name = stamp });
        var artX = await Post(c, "article", new { status = "draft", categoryId = cat, translations = new { en = new { title = stamp + "X" } } });
        var artY = await Post(c, "article", new { status = "draft", categoryId = cat, translations = new { en = new { title = stamp + "Y" } } });

        var ids = await IdsAsync(c, $"/api/items/article?filter%5Bcategory.name%5D%5B_eq%5D={stamp}&search={stamp}X");
        ids.Should().Contain(artX).And.NotContain(artY);
    }

    // Same question for a plain own-field filter combined with search on the TRANSLATABLE `title`
    // field (article's search path): both articles share the same title stamp; only one's status
    // matches the filter.
    [Fact]
    public async Task Scalar_filter_and_translatable_search_combine_as_and()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var stamp = "FS2" + Guid.NewGuid().ToString("N")[..8];
        var draftArt = await Post(c, "article", new { status = "draft", translations = new { en = new { title = stamp } } });
        var publishedArt = await Post(c, "article", new { status = "published", translations = new { en = new { title = stamp } } });

        var ids = await IdsAsync(c, $"/api/items/article?filter%5Bstatus%5D%5B_eq%5D=published&search={stamp}");
        ids.Should().Contain(publishedArt).And.NotContain(draftArt);
    }
}
