// tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
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
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
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
            OnQuery = (_, q, _) => new PagedResult(
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
            OnQuery = (_, q, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
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
            OnQuery = (_, q, loc) => { seen = loc; return new PagedResult([], 0, q.Limit, q.Offset); }
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
            OnQuery = (collection, q, _) => collection == "article"
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
            OnQuery = (collection, q, _) => collection == "article"
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
            OnQuery = (collection, q, _) => collection == "article"
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
}
