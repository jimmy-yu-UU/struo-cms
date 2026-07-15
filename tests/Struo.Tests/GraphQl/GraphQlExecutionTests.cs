// tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Executes real GraphQL queries through the dynamic schema (StruoTypeModule) against a
/// <see cref="FakeGraphQlDataSource"/> — the Task-7 root-resolver behaviour gate: list shape
/// (items/total), filter/sort/pagination/search flowing into the captured <see cref="QueryModel"/>,
/// single-by-id, single-missing -> null data, locale-argument forwarding, and the translations map
/// projection (regression guard for the ItemService-shape bug described below).
/// </summary>
public class GraphQlExecutionTests
{
    private static async Task<IRequestExecutor> ExecutorAsync(FakeGraphQlDataSource ds)
        => await new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddScoped<IGraphQlDataSource>(_ => ds)
            // FileByIdDataLoader's ctor takes StruoQueryOptions (Task-9 review fix: chunk the
            // batch fetch to MaxLimit-sized slices) — HotChocolate's ctx.DataLoader<T>() resolves
            // it via ActivatorUtilities against request services, so it must be registered here too.
            .AddSingleton(new StruoQueryOptions())
            // AddTypeModule<T>() resolves T via GetRequiredService<T>() against application
            // services (not schema-scoped activation) — must be registered explicitly (mirrors
            // GraphQlSchemaTests).
            .AddSingleton<StruoTypeModule>()
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddMutationType(d => d.Name("Mutation").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            // Mirrors GraphQlServiceCollectionExtensions.AddStruoGraphQl's production wiring so this
            // in-process executor can prove the over-depth negative below (this suite's
            // FakeGraphQlDataSource bypasses ItemService's own MaxRelationDepth check entirely, so
            // this document-validation-time rule is the only reachable depth guard here).
            .AddMaxExecutionDepthRule(12, skipIntrospectionFields: true)
            .BuildRequestExecutorAsync();

    /// <summary>
    /// Parses the operation JSON and returns the "data" element. Asserting through JsonDocument
    /// (rather than substring-matching the raw JSON text) keeps checks code-point exact regardless
    /// of whether the writer escapes non-ASCII characters as \uXXXX.
    /// </summary>
    private static JsonElement ParseData(IExecutionResult result)
    {
        var json = result.ToJson();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("errors", out var errors))
            throw new InvalidOperationException($"GraphQL execution returned errors: {errors}\nFull response: {json}");
        return doc.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task List_returns_items_and_total()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) => new PagedResult(
                [new Dictionary<string, object?> { ["id"] = "1", ["status"] = "published" }],
                42, q.Limit, q.Offset)
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(limit: 5) { items { id status } total } }");
        var data = ParseData(result);

        data.GetProperty("articles").GetProperty("total").GetInt32().Should().Be(42);
        var items = data.GetProperty("articles").GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("id").GetString().Should().Be("1");
        items[0].GetProperty("status").GetString().Should().Be("published");
    }

    [Fact]
    public async Task List_passes_filter_sort_pagination_into_QueryModel()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        // Regression guard: HotChocolate populates the runtime dictionary for the nested op-input
        // (StringFilter) with EVERY declared field, not just "eq" — unset ones default to null.
        // FilterInputTranslator.AddField must skip those, or a single `{ eq: ... }` filter explodes
        // into 8 ANDed comparisons (Neq/In/Nin/Contains/StartsWith/EndsWith/NNull, all value=null).
        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(limit: 3, offset: 6, sort: [\"-status\"], search: \"hi\", filter: { status: { eq: \"published\" } }) { total } }");
        ParseData(result); // asserts no errors

        captured.Should().NotBeNull();
        captured!.Limit.Should().Be(3);
        captured.Offset.Should().Be(6);
        captured.Search.Should().Be("hi");
        captured.Sort.Should().ContainSingle();
        captured.Sort[0].Field.Should().Be("status");
        captured.Sort[0].Descending.Should().BeTrue();
        captured.Filter.Should().NotBeNull();
        var cmp = captured.Filter.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("status");
        cmp.Op.Should().Be(QueryOperator.Eq);
        cmp.Value.Should().Be("published");
    }

    [Fact]
    public async Task Single_returns_null_maps_to_null_data()
    {
        var ds = new FakeGraphQlDataSource { OnGet = (_, _, _, _) => null };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync("{ article(id: \"x\") { id } }");
        var data = ParseData(result);

        data.GetProperty("article").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Locale_argument_is_forwarded()
    {
        string? seen = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, loc, _) => { seen = loc; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync("{ articles(locale: \"zh-TW\") { total } }");
        ParseData(result); // asserts no errors

        seen.Should().Be("zh-TW");
    }

    /// <summary>
    /// Regression guard: <c>ItemService.OverlayTranslationsAsync</c> projects "translations" as the
    /// concrete type <c>Dictionary&lt;string, Dictionary&lt;string, object?&gt;&gt;</c> — which does
    /// NOT implement the generic <c>IDictionary&lt;string, object?&gt;</c> (.NET generic dictionary
    /// interfaces are not covariant in the value type), so a guard written against that generic
    /// interface silently misses and returns <c>[]</c>. Feeding the resolver the exact ItemService
    /// shape (not a hand-shaped IDictionary&lt;string, object?&gt;) must yield both locales, with
    /// their field maps intact — including a non-ASCII (CJK) value surviving byte-for-byte.
    /// </summary>
    [Fact]
    public async Task Translations_use_ItemService_projection_shape_and_preserve_all_locales_including_cjk()
    {
        var translations = new Dictionary<string, Dictionary<string, object?>>
        {
            ["en"] = new() { ["title"] = "Hello" },
            ["zh-TW"] = new() { ["title"] = "哈囉" },
        };
        var ds = new FakeGraphQlDataSource
        {
            OnGet = (_, _, _, _) => new Dictionary<string, object?>
            {
                ["id"] = "1",
                ["translations"] = translations,
            }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ article(id: \"1\") { translations { locale fields } } }");
        var data = ParseData(result);

        var list = data.GetProperty("article").GetProperty("translations");
        list.GetArrayLength().Should().Be(2,
            "both locales must survive — the disposed/empty-guard bug returned [] for this exact shape");

        var byLocale = list.EnumerateArray()
            .ToDictionary(e => e.GetProperty("locale").GetString()!, e => e.GetProperty("fields"));

        byLocale.Should().ContainKey("en");
        byLocale["en"].GetProperty("title").GetString().Should().Be("Hello");

        byLocale.Should().ContainKey("zh-TW");
        var zh = byLocale["zh-TW"].GetProperty("title").GetString();
        zh.Should().Be("哈囉");
        // Code-point exact: guard against lossy transcoding anywhere along the resolver/JSON path.
        zh!.EnumerateRunes().Select(r => r.Value).Should().Equal("哈囉".EnumerateRunes().Select(r => r.Value));
    }

    /// <summary>
    /// Task 9 N+1 regression lock: two article rows share the same heroImageId — the File
    /// resolver must batch every requested id across all sibling rows into exactly ONE
    /// "file" QueryAsync call (not one per row) via <see cref="FileFieldResolvers"/>'s
    /// BatchDataLoader, and the returned File node's fields must be resolvable.
    /// </summary>
    [Fact]
    public async Task HeroImage_resolves_file_and_batches_one_query()
    {
        var fileId = Guid.NewGuid();
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (collection, q, _, _) => collection == "article"
                ? new PagedResult(new IReadOnlyDictionary<string, object?>[]
                    {
                        new Dictionary<string, object?> { ["id"] = "1", ["heroImageId"] = fileId },
                        new Dictionary<string, object?> { ["id"] = "2", ["heroImageId"] = fileId },
                    }, 2, q.Limit, q.Offset)
                : new PagedResult(new IReadOnlyDictionary<string, object?>[]
                    {
                        new Dictionary<string, object?> { ["id"] = fileId, ["title"] = "pic" }
                    }, 1, q.Limit, q.Offset)
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles { items { id heroImage { id title } } } }");
        var json = result.ToJson();
        json.Should().Contain("\"title\": \"pic\"");
        json.Should().NotContain("errors");

        // Batching: exactly one article query + one file query (not one file query per row).
        ds.QueryCollections.Count(c => c == "file").Should().Be(1);
    }

    [Fact]
    public async Task Missing_file_resolves_to_null()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (collection, q, _, _) => collection == "article"
                ? new PagedResult(new IReadOnlyDictionary<string, object?>[]
                    { new Dictionary<string, object?> { ["id"] = "1", ["heroImageId"] = Guid.NewGuid() } }, 1, q.Limit, q.Offset)
                : new PagedResult([], 0, q.Limit, q.Offset) // file not found
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync("{ articles { items { heroImage { id } } } }");
        var json = result.ToJson();
        json.Should().Contain("\"heroImage\": null");
        json.Should().NotContain("errors");
    }

    /// <summary>
    /// Task-9 review fix regression (Minor): the scalar path (<c>heroImage</c>) already covers
    /// null-on-missing, but the list-shaped field (<c>galleryFiles</c>) — <see
    /// cref="FileFieldResolvers.ResolveList"/> — had no direct execution-level test of its two
    /// defining behaviours: it must resolve ids in the REQUESTED order (not whatever order the
    /// batched "file" query happens to return rows in) and DROP any id missing from the "file"
    /// collection, rather than erroring or emitting null placeholders.
    /// </summary>
    [Fact]
    public async Task GalleryFiles_preserves_order_and_drops_missing_ids()
    {
        var present1 = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var present2 = Guid.NewGuid();

        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (collection, q, _, _) => collection == "article"
                ? new PagedResult(new IReadOnlyDictionary<string, object?>[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["id"] = "1",
                            ["gallery"] = new List<Guid> { present1, missing, present2 },
                        },
                    }, 1, q.Limit, q.Offset)
                // Deliberately returned in the OPPOSITE order from the gallery list, and missing
                // is simply absent — the resolver's output order must follow the requested id
                // sequence, not the "file" query's row order.
                : new PagedResult(new IReadOnlyDictionary<string, object?>[]
                    {
                        new Dictionary<string, object?> { ["id"] = present2, ["title"] = "second" },
                        new Dictionary<string, object?> { ["id"] = present1, ["title"] = "first" },
                    }, 2, q.Limit, q.Offset)
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles { items { id galleryFiles { id title } } } }");
        var data = ParseData(result);

        var files = data.GetProperty("articles").GetProperty("items")[0].GetProperty("galleryFiles");
        files.GetArrayLength().Should().Be(2, "the missing id must be dropped, not resolved as null/error");
        files[0].GetProperty("title").GetString().Should().Be("first", "order must follow the gallery list, not the file query's row order");
        files[1].GetProperty("title").GetString().Should().Be("second");

        // Batching still holds for the list path too: one "file" query for the whole request.
        ds.QueryCollections.Count(c => c == "file").Should().Be(1);
    }

    /// <summary>
    /// Task 10: selecting a relation sub-field (<c>category { name }</c>) on the list element must
    /// build a <see cref="DeepSpec"/> containing exactly that relation name and pass it into the
    /// captured <see cref="QueryModel"/> — the fake data source then returns an already-nested
    /// "category" dict on the article row (mirroring ItemService's deep-expansion shape), which the
    /// schema's relation field (a plain <c>ParentDict(ctx).GetValueOrDefault(rel.Name)</c> pure
    /// resolver — see CollectionSchemaBuilder) must surface as-is, one level deep.
    /// </summary>
    [Fact]
    public async Task Selecting_relation_sets_Deep_and_nests_value()
    {
        DeepSpec? deepSeen = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) =>
            {
                deepSeen = q.Deep;
                var row = new Dictionary<string, object?>
                {
                    ["id"] = "1",
                    ["category"] = new Dictionary<string, object?> { ["id"] = "9", ["name"] = "News" },
                };
                return new PagedResult(new IReadOnlyDictionary<string, object?>[] { row }, 1, q.Limit, q.Offset);
            }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles { items { id category { name } } } }");
        var data = ParseData(result);

        deepSeen.Should().NotBeNull();
        deepSeen!.Relations.Should().ContainKey("category");
        data.GetProperty("articles").GetProperty("items")[0]
            .GetProperty("category").GetProperty("name").GetString().Should().Be("News");
    }

    /// <summary>
    /// 8c.3a: selecting a relation sub-field OF a relation (<c>category { name parent { name } }</c>,
    /// depth 2) must build a NESTED <see cref="DeepSpec"/> — <c>category</c>'s own
    /// <see cref="DeepRelationSpec.Deep"/> must itself contain a <c>parent</c> entry — not a flat
    /// depth-1 tree (pre-8c.3a, <c>SelectionRelations</c> only ever looked at the element type's
    /// direct children, so <c>parent</c> was silently dropped and would have resolved to null).
    /// The fake data source mirrors ItemService's deep-expansion shape by pre-nesting "parent" inside
    /// "category" on the row; the schema's relation field is a plain pass-through pure resolver (see
    /// CollectionSchemaBuilder), so it surfaces whatever shape the data source returns at every level.
    /// </summary>
    [Fact]
    public async Task Nested_relation_selection_resolves_depth_two()
    {
        DeepSpec? deepSeen = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) =>
            {
                deepSeen = q.Deep;
                var row = new Dictionary<string, object?>
                {
                    ["id"] = "1",
                    ["category"] = new Dictionary<string, object?>
                    {
                        ["id"] = "9",
                        ["name"] = "Child",
                        ["parent"] = new Dictionary<string, object?> { ["id"] = "8", ["name"] = "Parent" },
                    },
                };
                return new PagedResult(new IReadOnlyDictionary<string, object?>[] { row }, 1, q.Limit, q.Offset);
            }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles { items { id category { name parent { name } } } } }");
        var data = ParseData(result);

        deepSeen.Should().NotBeNull();
        deepSeen!.Relations.Should().ContainKey("category");
        var categoryDeep = deepSeen.Relations["category"].Deep;
        categoryDeep.Should().NotBeNull("a depth-2 selection must nest a Deep tree under category, not stay flat");
        categoryDeep!.Relations.Should().ContainKey("parent");

        var item = data.GetProperty("articles").GetProperty("items")[0];
        item.GetProperty("category").GetProperty("name").GetString().Should().Be("Child");
        item.GetProperty("category").GetProperty("parent").GetProperty("name").GetString().Should().Be("Parent");
    }

    /// <summary>
    /// 8c.3a negative: <see cref="StruoQueryOptions.MaxRelationDepth"/> (5) is enforced by
    /// ItemService, which this suite's <see cref="FakeGraphQlDataSource"/> bypasses entirely (no real
    /// ItemService sits in the call path), so a client selection nested deep enough to matter here
    /// must instead be caught by HotChocolate's own <c>AddMaxExecutionDepthRule(12)</c> — added to
    /// <see cref="ExecutorAsync"/> above to mirror the production wiring
    /// (<see cref="Struo.Api.GraphQl.GraphQlServiceCollectionExtensions.AddStruoGraphQl"/>) — which
    /// runs at document-validation time, before any resolver (including the now-recursive
    /// <see cref="CollectionResolvers.SelectionDeepSpec"/>) ever executes. This proves the recursive
    /// selection-walk introduced by 8c.3a has no runaway/unbounded behaviour reachable from a client: an
    /// over-deep query is rejected up front, with no partial data alongside the error.
    /// </summary>
    [Fact]
    public async Task Over_depth_nested_selection_is_rejected()
    {
        const int nestedParents = 20; // category is self-referential; comfortably past both the
                                       // engine's MaxRelationDepth (5, unreachable here) and HotChocolate's
                                       // AddMaxExecutionDepthRule(12, added to ExecutorAsync above).
        var query = "{ categories { items { " +
                    string.Concat(Enumerable.Repeat("parent { ", nestedParents)) +
                    "id" +
                    string.Concat(Enumerable.Repeat(" }", nestedParents)) +
                    " } } }";

        var result = await (await ExecutorAsync(new FakeGraphQlDataSource())).ExecuteAsync(query);
        var json = result.ToJson();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue(
            $"expected the max-execution-depth rule to reject this query; full response: {json}");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        doc.RootElement.TryGetProperty("data", out _).Should().BeFalse(
            "a validation-time rejection must not leak partial data alongside the error");
    }

    /// <summary>
    /// Final-review fix regression: HotChocolate merges non-aliased duplicate selections, but
    /// ALIASED selections of the SAME relation (<c>a: category</c> / <c>b: category</c>) stay
    /// distinct child selections that both report <c>Field.Name == "category"</c>. Before the fix,
    /// <see cref="CollectionResolvers"/>'s selection-to-relation-name projection returned
    /// <c>["category", "category"]</c>, which <see cref="GraphQlQueryBuilder.BuildQuery"/> fed into
    /// <c>ToDictionary</c> -> <see cref="ArgumentException"/> (duplicate key) -> masked
    /// INTERNAL_SERVER_ERROR for an otherwise-legal query. The relation names must be de-duplicated
    /// before reaching BuildQuery so aliasing a relation twice degrades to one Deep entry, not a crash.
    /// </summary>
    [Fact]
    public async Task Aliased_duplicate_relation_selection_does_not_crash_and_resolves_both_aliases()
    {
        DeepSpec? deepSeen = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) =>
            {
                deepSeen = q.Deep;
                var row = new Dictionary<string, object?>
                {
                    ["id"] = "1",
                    ["category"] = new Dictionary<string, object?> { ["id"] = "9", ["name"] = "News" },
                };
                return new PagedResult(new IReadOnlyDictionary<string, object?>[] { row }, 1, q.Limit, q.Offset);
            }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles { items { a: category { name } b: category { name } } } }");
        var data = ParseData(result); // asserts no errors — would throw INTERNAL_SERVER_ERROR pre-fix

        deepSeen.Should().NotBeNull();
        deepSeen!.Relations.Should().ContainKey("category");
        deepSeen.Relations.Should().HaveCount(1, "the duplicate aliased selection must collapse to one Deep entry");

        var item = data.GetProperty("articles").GetProperty("items")[0];
        item.GetProperty("a").GetProperty("name").GetString().Should().Be("News");
        item.GetProperty("b").GetProperty("name").GetString().Should().Be("News");
    }

    /// <summary>Task 10: no relation sub-field selected -> Deep stays null (no over-fetching).</summary>
    [Fact]
    public async Task Relation_not_selected_leaves_Deep_null()
    {
        DeepSpec? deepSeen = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) =>
            {
                deepSeen = q.Deep;
                return new PagedResult(
                    new IReadOnlyDictionary<string, object?>[] { new Dictionary<string, object?> { ["id"] = "1" } },
                    1, q.Limit, q.Offset);
            }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync("{ articles { items { id } } }");
        ParseData(result); // asserts no errors

        deepSeen.Should().BeNull();
    }

    /// <summary>
    /// Task 10 (closes the deferred Task 1 end-to-end error-code coverage): a
    /// <see cref="PermissionDeniedException"/> thrown out of the data source must surface as a
    /// GraphQL error with <c>extensions.code == "FORBIDDEN"</c>, which requires
    /// <see cref="StruoErrorFilter"/> to be registered. Per the Task-1 comment in
    /// GraphQlServiceCollectionExtensions, the filter is registered via the plain-IServiceCollection
    /// <c>AddErrorFilter&lt;T&gt;()</c> overload (resolves against application services, where
    /// ILogger&lt;T&gt; is available) rather than chained on the request-executor builder (whose
    /// schema-services container excludes logging and would fail to activate the filter).
    /// </summary>
    /// <summary>
    /// Live-gate regression (HC0053): <c>ItemService.Project</c> emits a Repeater field's value as
    /// the entity's raw <c>List&lt;TChild&gt;</c> of POCOs (e.g. <c>List&lt;FaqItem&gt;</c>) — NOT a
    /// list of dictionaries. Before the fix, <c>BuildRepeaterItemType</c>'s sub-field resolvers cast
    /// the parent straight to <c>IReadOnlyDictionary&lt;string,object?&gt;</c>, which throws
    /// HC0053 ("unable to cast the parent type") when the parent is actually a POCO. The fix must
    /// read sub-fields via reflection (mirroring how <see cref="StruoTypeModule"/>'s TagItemType
    /// already tolerates a POCO parent), so this must resolve cleanly against a real POCO row —
    /// not the hand-shaped dictionary fixtures the rest of this suite uses.
    /// </summary>
    [Fact]
    public async Task Repeater_subfields_resolve_from_POCO_children()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnGet = (_, _, _, _) => new Dictionary<string, object?>
            {
                ["id"] = "1",
                ["faqs"] = new List<object> { new FaqRow("Q1", "A1") },
            }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ article(id: \"1\") { faqs { question answer } } }");
        var data = ParseData(result); // asserts no errors — would throw HC0053 pre-fix

        var faqs = data.GetProperty("article").GetProperty("faqs");
        faqs.GetArrayLength().Should().Be(1);
        faqs[0].GetProperty("question").GetString().Should().Be("Q1");
        faqs[0].GetProperty("answer").GetString().Should().Be("A1");
    }

    // PascalCase properties mirroring a real Repeater sub-field POCO (e.g. Struo.Sample.Blog.FaqItem)
    // — the sub-field names in the GraphQL query ("question"/"answer") are camelCase.
    private sealed record FaqRow(string Question, string Answer);

    // ARC-3: this in-process executor has NO HttpContext, so the shared DomainErrorMap treats the
    // caller as unauthenticated and PermissionDenied surfaces as UNAUTHORIZED (REST-parity), not the
    // old unconditional FORBIDDEN. AddHttpContextAccessor is required so the filter can be activated.
    [Fact]
    public async Task PermissionDenied_without_http_context_surfaces_as_UNAUTHORIZED_code()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, _, _, _) => throw new PermissionDeniedException("no read")
        };

        var services = new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddScoped<IGraphQlDataSource>(_ => ds)
            .AddSingleton(new StruoQueryOptions())
            .AddSingleton<StruoTypeModule>()
            .AddHttpContextAccessor()
            .AddLogging();
        services.AddErrorFilter<StruoErrorFilter>(); // plain-IServiceCollection overload (see summary above)

        var executor = await services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddMutationType(d => d.Name("Mutation").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            .BuildRequestExecutorAsync();

        var json = (await executor.ExecuteAsync("{ articles { total } }")).ToJson();

        json.Should().Contain("UNAUTHORIZED");
    }

    [Fact]
    public async Task Nested_M2O_relation_filter_arrives_as_dotted_comparison()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(filter: { category: { name: { eq: \"Tech\" } } }) { total } }");
        ParseData(result); // asserts no errors

        var cmp = captured!.Filter.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("category.name");
        cmp.Op.Should().Be(QueryOperator.Eq);
        cmp.Value.Should().Be("Tech");
    }

    [Fact]
    public async Task Multi_hop_relation_filter_arrives_as_multi_dotted_comparison()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(filter: { category: { parent: { name: { eq: \"Root\" } } } }) { total } }");
        ParseData(result);

        captured!.Filter.Should().BeOfType<ComparisonFilter>()
            .Which.FieldPath.Should().Be("category.parent.name");
    }

    [Fact]
    public async Task Own_field_and_nested_relation_filter_are_anded()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(filter: { status: { eq: \"published\" }, category: { name: { eq: \"Tech\" } } }) { total } }");
        ParseData(result);

        var logical = captured!.Filter.Should().BeOfType<LogicalFilter>().Subject;
        logical.Children.OfType<ComparisonFilter>().Select(c => c.FieldPath)
            .Should().Contain(new[] { "status", "category.name" });
    }

    [Fact]
    public async Task Cross_relation_sort_token_flows_into_QueryModel()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(sort: [\"category.name\", \"-category.parent.name\"]) { total } }");
        ParseData(result);

        captured!.Sort.Should().HaveCount(2);
        captured.Sort[0].Field.Should().Be("category.name");
        captured.Sort[0].Descending.Should().BeFalse();
        captured.Sort[1].Field.Should().Be("category.parent.name");
        captured.Sort[1].Descending.Should().BeTrue();
    }

    [Fact]
    public async Task M2M_relation_filter_arrives_as_dotted_comparison()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(filter: { tags: { name: { eq: \"AI\" } } }) { total } }");
        ParseData(result); // asserts no errors

        var cmp = captured!.Filter.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("tags.name");
        cmp.Op.Should().Be(QueryOperator.Eq);
        cmp.Value.Should().Be("AI");
    }

    [Fact]
    public async Task O2M_relation_filter_arrives_as_dotted_comparison()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        // categories filtered by a field on their O2M articles (ANY/EXISTS).
        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ categories(filter: { articles: { status: { eq: \"published\" } } }) { total } }");
        ParseData(result);

        var cmp = captured!.Filter.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("articles.status");
        cmp.Value.Should().Be("published");
    }

    [Fact]
    public async Task Mixed_kind_multi_hop_relation_filter_arrives_as_multi_dotted_comparison()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        // category -> (O2M) articles -> (M2O) category -> name : composes to-many + to-one.
        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ categories(filter: { articles: { category: { name: { eq: \"Root\" } } } }) { total } }");
        ParseData(result);

        captured!.Filter.Should().BeOfType<ComparisonFilter>()
            .Which.FieldPath.Should().Be("articles.category.name");
    }

    [Fact]
    public async Task Own_field_and_to_many_relation_filter_are_anded()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(filter: { status: { eq: \"published\" }, tags: { name: { eq: \"AI\" } } }) { total } }");
        ParseData(result);

        var logical = captured!.Filter.Should().BeOfType<LogicalFilter>().Subject;
        logical.Children.OfType<ComparisonFilter>().Select(c => c.FieldPath)
            .Should().Contain(new[] { "status", "tags.name" });
    }
}
