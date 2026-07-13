# Phase 8c.3b — Nested-list `filter`/`sort`/`limit`/`offset` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a client filter/sort/limit/offset a nested to-many relation list, on both the GraphQL read API and the REST `deep` JSON envelope, at every nesting level up to `MaxRelationDepth`.

**Architecture:** Extend the recursive `DeepRelationSpec` (Domain) with `Filter`/`Sort`/`Offset` (additive init-only props; `Limit` already exists but was unused). The REST envelope parser and GraphQL selection walker populate them. `ItemService.ValidateDeepTree` validates them (to-many only, whitelist filter paths, own-field sort, non-negative bounds) before execution. `RelationExpander` (Infrastructure) pushes `filter` into the existing batched fetch via `RelationFilterResolver` rewrite + a new filtered repository method, and applies `sort`/`limit`/`offset` **in-memory per parent group** — preserving the N+1-safe batched invariant.

**Tech Stack:** .NET 10 / C#, SqlSugarCore, HotChocolate 16.4.0, xUnit + AwesomeAssertions, PostgreSQL (runtime) / SQLite (tests).

## Global Constraints

- **Dependency rule (§2):** Domain → nothing; Application → Domain; Infrastructure → Application+Domain; Api → Application+Infrastructure. Framework code never references `samples/*`. Domain stays package-free.
- **No vendor SQL (§17.4):** all DB access via SqlSugar ORM. No raw window functions / LATERAL. `filter` push-down reuses `ConditionalModelTranslator`; `sort`/`limit`/`offset` are in-memory.
- **No new NuGet packages.** `Directory.Packages.props` unchanged. No `samples/*` change.
- **N+1-safe invariant:** one follow-up query per relation-node per level, independent of row count and of whether args are present. Locked by the extended `DeepNestingBatchingTests` count assertion (== relation-node count).
- **Back-compat:** an all-null spec (no args) behaves exactly like 8c.3a — nested lists return **all** rows in default order. Omitting `limit` returns all rows (no default truncation).
- **Whitelist (§17.7):** nested filter/sort paths are validated against the **target** collection's metadata before execution; unknown field/relation → `BAD_USER_INPUT`.
- **Build gate:** `dotnet build -warnaserror` must stay at **0 warnings**. Backend `dotnet test` all green (baseline **549**). Frontend untouched (**237**, not run/changed here).
- **Camel/CLR reads:** `readProp(entity, name)` resolves camelCase or CLR names case-insensitively; sort field tokens are camelCase (e.g. `name`, `-createdAt`).

## File Structure

**Modify:**
- `src/Struo.Domain/Query/DeepSpec.cs` — `DeepRelationSpec` gains `Filter`/`Sort`/`Offset` init-only props.
- `src/Struo.Application/Query/QueryParser.cs` — `ParseDeepObject` reads `filter`/`sort`/`offset` envelope keys.
- `src/Struo.Application/Query/IItemRepository.cs` — add `QueryWhereInFilteredAsync`.
- `src/Struo.Application/Query/IRelationExpander.cs` — `ExpandAsync` gains `string? locale = null`.
- `src/Struo.Application/Query/ItemService.cs` — `ValidateDeepTree` per-level arg validation; thread `locale` into `ExpandDeepAsync` → expander.
- `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` — implement `QueryWhereInFilteredAsync`.
- `src/Struo.Infrastructure/Query/RelationExpander.cs` — `IRelationFilterResolver` dep + `locale`; consume filter/sort/limit/offset.
- `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs` — declare 4 args on O2M/M2M relation fields.
- `src/Struo.Api/GraphQl/CollectionResolvers.cs` — `BuildDeep` reads selection args into the spec.

**Modify (tests support):**
- `tests/Struo.Tests/Support/CountingItemRepository.cs` — implement + count `QueryWhereInFilteredAsync`.
- `tests/Struo.Tests/Query/DeepNestingBatchingTests.cs` — construct `RelationExpander` with the resolver; add N+1-with-args test.
- `tests/Struo.Tests/Query/DeleteRestrictWithGuidPkTests.cs` — `StubRepo` gains a `QueryWhereInFilteredAsync` stub.

**Create (tests):**
- `tests/Struo.Tests/Query/NestedListArgsParseTests.cs` — envelope parse unit tests.
- `tests/Struo.Tests/Query/NestedListArgsValidationTests.cs` — validation → 400 (REST).
- `tests/Struo.Tests/Query/NestedListArgsExpansionTests.cs` — engine filter/sort/limit/offset (REST).
- `tests/Struo.Tests/GraphQl/NestedListArgsSchemaTests.cs` — schema arg presence/absence.
- `tests/Struo.Tests/GraphQl/NestedListArgsExecutionTests.cs` — GraphQL execution round-trip (inline + variable).

**Docs:** `docs/ROADMAP.md`, `docs/guide/*`.

---

### Task 1: Domain — `DeepRelationSpec` gains `Filter`/`Sort`/`Offset`

**Files:**
- Modify: `src/Struo.Domain/Query/DeepSpec.cs`
- Test: `tests/Struo.Tests/Query/DeepSpecTests.cs`

**Interfaces:**
- Produces: `DeepRelationSpec` with existing positional `(IReadOnlyList<string>? Fields, int? Limit, DeepSpec? Deep = null)` **unchanged**, plus init-only `FilterNode? Filter`, `IReadOnlyList<SortField>? Sort`, `int? Offset`. Every existing construction site keeps compiling untouched.

- [ ] **Step 1: Write the failing test** (append to `DeepSpecTests.cs`)

```csharp
    [Fact]
    public void DeepRelationSpec_carries_nested_list_args()
    {
        var filter = new ComparisonFilter("name", QueryOperator.Eq, "x");
        var sort = new List<SortField> { new("name", false) };
        var spec = new DeepRelationSpec(null, 5, null) { Filter = filter, Sort = sort, Offset = 2 };

        spec.Filter.Should().BeSameAs(filter);
        spec.Sort.Should().ContainSingle().Which.Field.Should().Be("name");
        spec.Limit.Should().Be(5);
        spec.Offset.Should().Be(2);
    }

    [Fact]
    public void DeepRelationSpec_list_args_default_null()
    {
        var spec = new DeepRelationSpec(null, null);
        spec.Filter.Should().BeNull();
        spec.Sort.Should().BeNull();
        spec.Offset.Should().BeNull();
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DeepSpecTests.DeepRelationSpec_carries_nested_list_args"`
Expected: FAIL — compile error, `DeepRelationSpec` has no `Filter`/`Sort`/`Offset`.

- [ ] **Step 3: Write minimal implementation** — replace the `DeepRelationSpec` record in `DeepSpec.cs`

```csharp
/// <summary>
/// Per-relation deep-expansion options: an optional field whitelist for the expanded target rows,
/// per-parent nested-list <see cref="Filter"/> (cross-relation, resolved against the target
/// collection), own-field <see cref="Sort"/>, per-parent <see cref="Limit"/>/<see cref="Offset"/>,
/// and an optional nested <see cref="DeepSpec"/> for multi-level (depth &gt; 1) expansion.
/// Filter/Sort/Limit/Offset apply to to-many (O2M/M2M) list relations only; they are null for M2O.
/// The positional parameters are unchanged from 8c.3a; Filter/Sort/Offset are additive init-only
/// properties so every existing construction site compiles unchanged.
/// </summary>
public sealed record DeepRelationSpec(
    IReadOnlyList<string>? Fields, int? Limit, DeepSpec? Deep = null)
{
    public FilterNode? Filter { get; init; }
    public IReadOnlyList<SortField>? Sort { get; init; }
    public int? Offset { get; init; }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DeepSpecTests"`
Expected: PASS (all DeepSpecTests, including the two existing ones).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Domain/Query/DeepSpec.cs tests/Struo.Tests/Query/DeepSpecTests.cs
git commit -m "feat(query): DeepRelationSpec gains nested-list Filter/Sort/Offset (8c.3b)"
```

---

### Task 2: REST envelope parsing — `filter`/`sort`/`offset` keys

**Files:**
- Modify: `src/Struo.Application/Query/QueryParser.cs:50-69` (`ParseDeepObject`)
- Create: `tests/Struo.Tests/Query/NestedListArgsParseTests.cs`

**Interfaces:**
- Consumes: `DeepRelationSpec` init props (Task 1); existing `ParseFilter(JsonElement)` and `ParseSortToken(string)` in `QueryParser`.
- Produces: `ParseDeepObject` now populates `Filter`/`Sort`/`Offset` (and the already-parsed `Limit`) on each per-relation spec, recursively.

- [ ] **Step 1: Write the failing test** (`NestedListArgsParseTests.cs`)

```csharp
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class NestedListArgsParseTests
{
    private static QueryModel Parse(string json) =>
        QueryParser.ParseEnvelope(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Envelope_parses_nested_filter_sort_limit_offset()
    {
        var model = Parse("""
        { "deep": { "tags": {
            "filter": { "name": { "_contains": "AI" } },
            "sort": ["name", "-createdAt"],
            "limit": 5,
            "offset": 2
        } } }
        """);

        var spec = model.Deep!.Relations["tags"];
        spec.Filter.Should().BeOfType<ComparisonFilter>()
            .Which.FieldPath.Should().Be("name");
        spec.Sort!.Select(s => (s.Field, s.Descending))
            .Should().Equal(("name", false), ("createdAt", true));
        spec.Limit.Should().Be(5);
        spec.Offset.Should().Be(2);
    }

    [Fact]
    public void Envelope_parses_args_at_nested_levels()
    {
        var model = Parse("""
        { "deep": { "category": { "deep": { "articles": { "limit": 3 } } } } }
        """);

        var articles = model.Deep!.Relations["category"].Deep!.Relations["articles"];
        articles.Limit.Should().Be(3);
    }

    [Fact]
    public void Envelope_without_args_leaves_them_null()
    {
        var model = Parse("""{ "deep": { "tags": {} } }""");
        var spec = model.Deep!.Relations["tags"];
        spec.Filter.Should().BeNull();
        spec.Sort.Should().BeNull();
        spec.Limit.Should().BeNull();
        spec.Offset.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsParseTests"`
Expected: FAIL — `spec.Filter`/`Sort`/`Offset` are null (not parsed yet).

- [ ] **Step 3: Write minimal implementation** — replace `ParseDeepObject` in `QueryParser.cs`

```csharp
    private static DeepSpec? ParseDeepObject(JsonElement d)
    {
        var map = new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in d.EnumerateObject())
        {
            IReadOnlyList<string>? fields = null;
            int? limit = null;
            int? offset = null;
            FilterNode? filter = null;
            List<SortField>? sort = null;
            DeepSpec? nested = null;
            if (rel.Value.ValueKind == JsonValueKind.Object)
            {
                if (rel.Value.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array)
                    fields = f.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                if (rel.Value.TryGetProperty("filter", out var fl) && fl.ValueKind == JsonValueKind.Object)
                    filter = ParseFilter(fl);
                if (rel.Value.TryGetProperty("sort", out var s) && s.ValueKind == JsonValueKind.Array)
                {
                    sort = new List<SortField>();
                    foreach (var item in s.EnumerateArray())
                        sort.Add(ParseSortToken(item.GetString() ?? ""));
                }
                if (rel.Value.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li)) limit = li;
                if (rel.Value.TryGetProperty("offset", out var o) && o.TryGetInt32(out var oi)) offset = oi;
                if (rel.Value.TryGetProperty("deep", out var nd) && nd.ValueKind == JsonValueKind.Object)
                    nested = ParseDeepObject(nd);
            }
            map[rel.Name] = new DeepRelationSpec(fields, limit, nested)
            {
                Filter = filter,
                Sort = sort,
                Offset = offset
            };
        }
        return map.Count == 0 ? null : new DeepSpec(map);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsParseTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/QueryParser.cs tests/Struo.Tests/Query/NestedListArgsParseTests.cs
git commit -m "feat(query): parse nested-list filter/sort/limit/offset from deep envelope (8c.3b)"
```

---

### Task 3: Validation — `ItemService.ValidateDeepTree` per-level arg checks

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs:244-256` (`ValidateDeepTree`)
- Create: `tests/Struo.Tests/Query/NestedListArgsValidationTests.cs`

**Interfaces:**
- Consumes: `graph.Resolve(coll, relName)` → `RelationMetadata?` (has `.Kind`, `.TargetCollection`); `Meta(coll)` → `CollectionMetadata`; `QueryValidator.Validate(QueryModel, CollectionMetadata, StruoQueryOptions, IRelationshipGraph, IMetadataProvider)`; `RelationPath.IsRelationPath(string)`; `options`, `graph`, `metadata` fields on `ItemService`.
- Produces: `ValidateDeepTree` throws `QueryException` (→ REST 400 / GraphQL `BAD_USER_INPUT`) on: any of Filter/Sort/Limit/Offset present on an M2O relation; a nested filter path failing target-collection whitelist; a nested sort token that is a relation/dotted path or unknown own field; negative Limit/Offset.

**Validation rules (exact):**
- **To-many only:** `rel.Kind == RelationKind.ManyToOne` and (`Filter != null || Sort != null || Limit != null || Offset != null`) → throw.
- **Filter whitelist:** run `QueryValidator.Validate(new QueryModel(null, relSpec.Filter, [], 0, 0, null), Meta(rel.TargetCollection), options, graph, metadata)` — reuses own-field + dotted-relation-path whitelist. Throws `QueryException` on unknown field/relation.
- **Own-field sort:** for each `SortField s` in `relSpec.Sort`: if `RelationPath.IsRelationPath(s.Field)` → throw `QueryException("Sort across relations is not supported for nested lists: '{s.Field}'.")`; else validate the field is a known own field by running it through `QueryValidator.Validate(new QueryModel(null, null, [s], 0, 0, null), Meta(rel.TargetCollection), options, graph, metadata)` — but since QueryValidator allows sortable to-one relation paths, we pre-reject relation paths ourselves (above) so only own-field/`id` tokens reach it.
- **Bounds:** `relSpec.Limit is < 0` → throw; `relSpec.Offset is < 0` → throw.

- [ ] **Step 1: Write the failing test** (`NestedListArgsValidationTests.cs`)

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class NestedListArgsValidationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task<HttpResponseMessage> Query(string col, object envelope)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        // seed one row so validation runs (ExpandDeepAsync validates before the entities.Count==0 guard,
        // but a row makes the whole path realistic).
        await c.PostAsJsonAsync("/api/items/category", new { name = "ValSeed" });
        return await c.PostAsJsonAsync($"/api/items/{col}/query",
            JsonSerializer.SerializeToElement(envelope));
    }

    [Fact]
    public async Task Args_on_m2o_relation_return_400()
    {
        // article.category is M2O (single object) — nested list args are illegal.
        var resp = await Query("article", new { deep = new { category = new { limit = 5 } } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_nested_filter_field_returns_400()
    {
        var resp = await Query("category",
            new { deep = new { articles = new { filter = new { ghostfield = new Dictionary<string, object> { ["_eq"] = "x" } } } } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Relation_path_nested_sort_returns_400()
    {
        var resp = await Query("category",
            new { deep = new { articles = new { sort = new[] { "category.name" } } } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Negative_nested_limit_returns_400()
    {
        var resp = await Query("category",
            new { deep = new { articles = new { limit = -1 } } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Valid_nested_own_field_args_return_200()
    {
        var resp = await Query("category",
            new { deep = new { articles = new { filter = new { status = new Dictionary<string, object> { ["_eq"] = "published" } }, sort = new[] { "status" }, limit = 3, offset = 0 } } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsValidationTests"`
Expected: FAIL — the illegal cases currently return 200 (no arg validation yet). (The engine does not yet consume args, so the valid case may also 200 — that one should pass once validation is added; the illegal ones are the RED signal.)

- [ ] **Step 3: Write minimal implementation** — replace `ValidateDeepTree` in `ItemService.cs`

```csharp
    private void ValidateDeepTree(string coll, DeepSpec spec, int depth)
    {
        if (depth > options.MaxRelationDepth)
            throw new QueryException(
                $"Relation nesting too deep (depth {depth}); the maximum is {options.MaxRelationDepth}.");
        foreach (var (relName, relSpec) in spec.Relations)
        {
            var rel = graph.Resolve(coll, relName)
                ?? throw new QueryException($"Unknown relation '{relName}' on '{coll}'.");

            var hasArgs = relSpec.Filter is not null || relSpec.Sort is not null
                          || relSpec.Limit is not null || relSpec.Offset is not null;
            if (hasArgs && rel.Kind == RelationKind.ManyToOne)
                throw new QueryException(
                    $"filter/sort/limit/offset are only supported on to-many relations; " +
                    $"'{relName}' on '{coll}' is many-to-one.");

            if (relSpec.Limit is < 0)
                throw new QueryException($"Nested 'limit' must not be negative for relation '{relName}'.");
            if (relSpec.Offset is < 0)
                throw new QueryException($"Nested 'offset' must not be negative for relation '{relName}'.");

            var targetMeta = Meta(rel.TargetCollection);
            if (relSpec.Filter is not null)
                QueryValidator.Validate(
                    new QueryModel(null, relSpec.Filter, [], 0, 0, null),
                    targetMeta, options, graph, metadata);

            if (relSpec.Sort is not null)
                foreach (var s in relSpec.Sort)
                {
                    if (RelationPath.IsRelationPath(s.Field))
                        throw new QueryException(
                            $"Sort across relations is not supported for nested lists: '{s.Field}'.");
                    QueryValidator.Validate(
                        new QueryModel(null, null, [s], 0, 0, null),
                        targetMeta, options, graph, metadata);
                }

            if (relSpec.Deep is not null)
                ValidateDeepTree(rel.TargetCollection, relSpec.Deep, depth + 1);
        }
    }
```

Note: `QueryModel`'s positional shape is `(Fields, Filter, Sort, Limit, Offset, Search)` — verify against `src/Struo.Domain/Query/QueryModel.cs` and adjust the argument order if it differs. `QueryValidator.Validate` returns a clamped `QueryModel`; the return is discarded (we call it only for its throw-on-invalid side effect).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsValidationTests"`
Expected: PASS (all 5).

Also run the 8c.3a validation suite to confirm no regression:
Run: `dotnet test --filter "FullyQualifiedName~DeepNestingValidationTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/NestedListArgsValidationTests.cs
git commit -m "feat(query): validate nested-list args (to-many only, whitelist, bounds) (8c.3b)"
```

---

### Task 4: Repository — `QueryWhereInFilteredAsync`

**Files:**
- Modify: `src/Struo.Application/Query/IItemRepository.cs` (add method after `QueryWhereInAsync`)
- Modify: `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` (add impl near `QueryWhereInAsync`, `:298-337`)
- Modify: `tests/Struo.Tests/Support/CountingItemRepository.cs` (implement + count)
- Modify: `tests/Struo.Tests/Query/DeleteRestrictWithGuidPkTests.cs` (`StubRepo` stub)
- Test: `tests/Struo.Tests/Query/NestedListArgsExpansionTests.cs` (created here; direct repo test)

**Interfaces:**
- Produces: `Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(string collection, string property, IReadOnlyList<object> values, FilterNode? extraFilter, CancellationToken ct = default)` — fetches rows of `collection` where `property IN values` AND (`extraFilter` translated to the collection's columns). `null`/empty `values` → empty list. `null extraFilter` → identical to `QueryWhereInAsync`.

- [ ] **Step 1: Write the failing test** (`NestedListArgsExpansionTests.cs`, first test)

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class NestedListArgsExpansionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> Post(HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    [Fact]
    public async Task QueryWhereInFiltered_narrows_by_extra_filter()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "RepoFilterCat" });
        await Post(c, "article", new { status = "published", categoryId = cat, translations = new { en = new { title = "Pub" } } });
        await Post(c, "article", new { status = "draft", categoryId = cat, translations = new { en = new { title = "Drf" } } });

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IItemRepository>();

        var filter = new ComparisonFilter("status", QueryOperator.Eq, "published");
        var rows = await repo.QueryWhereInFilteredAsync(
            "article", "categoryId", new object[] { System.Guid.Parse(cat) }, filter);

        rows.Should().HaveCount(1);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsExpansionTests.QueryWhereInFiltered_narrows_by_extra_filter"`
Expected: FAIL — `IItemRepository` has no `QueryWhereInFilteredAsync`.

- [ ] **Step 3a: Add the interface method** (`IItemRepository.cs`, after `QueryWhereInAsync`)

```csharp
    /// <summary>
    /// Like <see cref="QueryWhereInAsync"/> but ANDs an additional own-collection filter
    /// (already relation-rewritten to own columns) into the batched WHERE. Used by nested-list
    /// expansion (8c.3b) to push a to-many list's filter into the single batched fetch.
    /// </summary>
    Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(
        string collection, string property, IReadOnlyList<object> values,
        FilterNode? extraFilter, CancellationToken ct = default);
```

(Add `using Struo.Domain.Query;` if not already present in `IItemRepository.cs`.)

- [ ] **Step 3b: Implement in `SqlSugarItemRepository.cs`** (add after `WhereInGenericAsync`, and add a cached method def alongside the others)

Add the cached method def near the other `*Def` fields (top of class):

```csharp
    private static readonly MethodInfo WhereInFilteredGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(WhereInFilteredGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(IReadOnlyList<object>), typeof(FilterNode), typeof(CancellationToken)])!;
```

Add the public method + generic helper:

```csharp
    public async Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(
        string collection, string property, IReadOnlyList<object> values,
        FilterNode? extraFilter, CancellationToken ct = default)
    {
        if (values.Count == 0) return [];
        var d = Descriptor(collection);
        var clrProperty = d.FieldToProperty.TryGetValue(property, out var p) ? p : property;
        var column = db.EntityMaintenance.GetDbColumnName(clrProperty, d.EntityType);

        var conditionals = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = column,
                ConditionalType = ConditionalType.In,
                FieldValue = string.Join(",", values.Select(v => v?.ToString())),
                CSharpTypeName = TypeNameOf(values.FirstOrDefault(v => v is not null))
            }
        };
        // AND the extra own-collection filter (already relation-rewritten). SqlSugar ANDs consecutive
        // IConditionalModel entries. ConditionalModelTranslator maps camelCase field paths -> columns.
        if (extraFilter is not null)
            conditionals.AddRange(ConditionalModelTranslator.Translate(extraFilter, null, [], d, db));

        var method = WhereInFilteredGenericAsyncDef.MakeGenericMethod(d.EntityType);
        return await (Task<IReadOnlyList<object>>)method.Invoke(this, [conditionals, ct])!;
    }

    private async Task<IReadOnlyList<object>> WhereInFilteredGenericAsync<T>(
        List<IConditionalModel> conditionals, CancellationToken ct) where T : class, new()
    {
        var rows = await db.Queryable<T>().Where(conditionals).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }
```

Note: the cached-def signature list uses `typeof(FilterNode)` only to *find* the method by name/arity; the actual private helper takes `List<IConditionalModel>`. Fix the reflection lookup to match the real helper signature — use the helper's real parameter types:

```csharp
    private static readonly MethodInfo WhereInFilteredGenericAsyncDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(WhereInFilteredGenericAsync),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(List<IConditionalModel>), typeof(CancellationToken)])!;
```

(Use this corrected version; delete the placeholder above.)

- [ ] **Step 3c: Implement in `CountingItemRepository.cs`** (count into `WhereInCalls` so the N+1 invariant test stays meaningful)

```csharp
    public Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(
        string collection, string property, IReadOnlyList<object> values,
        Struo.Domain.Query.FilterNode? extraFilter, CancellationToken ct = default)
    {
        System.Threading.Interlocked.Increment(ref _whereInCalls);
        return inner.QueryWhereInFilteredAsync(collection, property, values, extraFilter, ct);
    }
```

(Match the existing increment style in the file — if the existing methods use `_whereInCalls++` rather than `Interlocked`, mirror that exactly.)

- [ ] **Step 3d: Implement in `StubRepo`** (`DeleteRestrictWithGuidPkTests.cs`)

```csharp
        public Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(
            string collection, string property, IReadOnlyList<object> values,
            Struo.Domain.Query.FilterNode? extraFilter, System.Threading.CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<object>>(new List<object>());
```

(Match `StubRepo`'s existing member style — if it throws `NotImplementedException` for unused members, mirror that instead.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsExpansionTests.QueryWhereInFiltered_narrows_by_extra_filter"`
Expected: PASS.
Run: `dotnet build -warnaserror` — Expected: 0 warnings (all three `IItemRepository` impls satisfied).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/IItemRepository.cs src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs tests/Struo.Tests/Support/CountingItemRepository.cs tests/Struo.Tests/Query/DeleteRestrictWithGuidPkTests.cs tests/Struo.Tests/Query/NestedListArgsExpansionTests.cs
git commit -m "feat(query): add QueryWhereInFilteredAsync batched fetch-with-filter (8c.3b)"
```

---

### Task 5: Engine — expander plumbing (`IRelationFilterResolver` + `locale`)

**Files:**
- Modify: `src/Struo.Application/Query/IRelationExpander.cs` (add `string? locale = null`)
- Modify: `src/Struo.Infrastructure/Query/RelationExpander.cs` (constructor + `ExpandAsync` signature; no behaviour change yet)
- Modify: `src/Struo.Application/Query/ItemService.cs` (`ExpandDeepAsync` gains `locale`; thread from `QueryAsync`/`GetAsync`)
- Modify: `tests/Struo.Tests/Query/DeepNestingBatchingTests.cs` (construct expander with resolver)

**Interfaces:**
- Consumes: `IRelationFilterResolver` (DI-registered `RelationFilterResolver`).
- Produces: `IRelationExpander.ExpandAsync(..., string? locale = null, CancellationToken ct = default)`; `RelationExpander` ctor `(IItemRepository repository, RelationshipGraph graph, IRelationFilterResolver filterResolver)`.

- [ ] **Step 1: Update the interface** (`IRelationExpander.cs`) — add `locale` before `ct`:

```csharp
    Task<Dictionary<object, Dictionary<string, object?>>> ExpandAsync(
        string collection,
        IReadOnlyList<object> parents,
        DeepSpec deep,
        Func<string, object, IReadOnlyList<string>?, IReadOnlyDictionary<string, object?>> projectTarget,
        Func<object, object> parentId,
        Func<object, string, object?> readProp,
        string? locale = null,
        CancellationToken ct = default);
```

- [ ] **Step 2: Update `RelationExpander` signature only** (`RelationExpander.cs`) — constructor + method signature; leave the body's per-kind logic unchanged for now (filter/sort/limit consumed in Tasks 6-8). Inject **both** `IRelationFilterResolver` (filter push-down, Task 6) and `StruoQueryOptions` (MaxLimit clamp, Task 7) now, so no further constructor churn later. Both are DI-registered, so the `AddScoped<IRelationExpander, RelationExpander>()` registration resolves them automatically — no DI-extension edit needed.

```csharp
public sealed class RelationExpander(
    IItemRepository repository, RelationshipGraph graph,
    IRelationFilterResolver filterResolver, StruoQueryOptions options)
    : IRelationExpander
{
    public async Task<Dictionary<object, Dictionary<string, object?>>> ExpandAsync(
        string collection, IReadOnlyList<object> parents, DeepSpec deep,
        Func<string, object, IReadOnlyList<string>?, IReadOnlyDictionary<string, object?>> projectTarget,
        Func<object, object> parentId, Func<object, string, object?> readProp,
        string? locale = null, CancellationToken ct = default)
    {
        // ... existing body unchanged; the recursive self-call must forward `locale`:
        //     await ExpandAsync(rel.TargetCollection, distinct, spec.Deep, projectTarget, parentId, readProp, locale, ct);
```

Update the recursive call at `RelationExpander.cs:144-145` to pass `locale` before `ct`. `IRelationFilterResolver` is in `Struo.Application.Query` (already imported). Add `using Struo.Application.Configuration;` for `StruoQueryOptions`.

- [ ] **Step 3: Thread `locale` in `ItemService`** — `ExpandDeepAsync` gains a `locale` parameter and forwards it; both call sites pass the effective query locale.

Change `ExpandDeepAsync` signature (`:208-211`) and its expander call (`:228-229`):

```csharp
    private async Task ExpandDeepAsync(
        string collection, DeepSpec? deep,
        IReadOnlyList<object> entities, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string? locale, CancellationToken ct)
    {
        if (deep is null || deep.Relations.Count == 0) return;
        ValidateDeepTree(collection, deep, depth: 1);
        if (entities.Count == 0) return;

        var parentDesc = registry.Get(collection)!;
        object ParentId(object entity) =>
            ReadProp(entity, parentDesc.IdProperty)
            ?? throw new QueryException($"Cannot expand relations: a '{collection}' row has no id.");

        var nested = await expander.ExpandAsync(
            collection, entities, deep, ProjectFor, ParentId, ReadProp, locale, ct);

        for (var i = 0; i < entities.Count; i++)
        {
            var pid = ParentId(entities[i]);
            if (!nested.TryGetValue(pid, out var relMap)) continue;
            var dict = (Dictionary<string, object?>)rows[i];
            foreach (var (relName, value) in relMap) dict[relName] = value;
        }
    }
```

Update the `QueryAsync` call site (`:60`) to pass `queryLocale`:

```csharp
        await ExpandDeepAsync(collection, raw.Deep, entities, rows, queryLocale, ct);
```

Update the `GetAsync` call site (`:75`) — compute the effective locale first, then pass it:

```csharp
        var queryLocale = meta.Translation is not null ? (locale ?? languages.DefaultCode()) : null;
        await ExpandDeepAsync(collection, deep, [entity], [projected], queryLocale, ct);
```

- [ ] **Step 4: Fix the direct-construction test** (`DeepNestingBatchingTests.cs`) — resolve the filter resolver from DI and pass it, and forward the new `locale` positional in the `ExpandAsync` call.

Replace the expander construction (`:45-47`) and the `ExpandAsync` call (`:61-65`):

```csharp
        var counter = new CountingItemRepository(real);
        var relFilter = scope.ServiceProvider.GetRequiredService<IRelationFilterResolver>();
        var opts = scope.ServiceProvider.GetRequiredService<StruoQueryOptions>();
        var expander = new RelationExpander(counter, graph, relFilter, opts);
```

```csharp
        await expander.ExpandAsync(
            "category", parents, deep,
            projectTarget: (_, entity, _) => new Dictionary<string, object?> { ["id"] = ReadProp(entity, "id") },
            parentId: entity => ReadProp(entity, "id")!,
            readProp: ReadProp);
```

(The `ExpandAsync` call keeps working — `locale` defaults to null. Add `using Struo.Application.Query;` + `using Struo.Application.Configuration;` to the test file if `IRelationFilterResolver`/`StruoQueryOptions` are unresolved.)

- [ ] **Step 5: Run tests to verify no regression**

Run: `dotnet build -warnaserror` — Expected: 0 warnings.
Run: `dotnet test --filter "FullyQualifiedName~DeepNesting"`
Expected: PASS (batching count still 2; expansion/validation unchanged — pure plumbing, no behaviour change).

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Application/Query/IRelationExpander.cs src/Struo.Infrastructure/Query/RelationExpander.cs src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/DeepNestingBatchingTests.cs
git commit -m "refactor(query): thread locale + IRelationFilterResolver into RelationExpander (8c.3b)"
```

---

### Task 6: Engine — consume `filter` (push-down)

**Files:**
- Modify: `src/Struo.Infrastructure/Query/RelationExpander.cs` (O2M + M2M branches)
- Test: `tests/Struo.Tests/Query/NestedListArgsExpansionTests.cs` (add tests)

**Interfaces:**
- Consumes: `QueryWhereInFilteredAsync` (Task 4); `filterResolver.RewriteAsync(rel.TargetCollection, spec.Filter, locale, ct)` → own-collection `FilterNode?`.
- Produces: O2M and M2M nested lists narrowed by `spec.Filter`.

- [ ] **Step 1: Write the failing tests** (append to `NestedListArgsExpansionTests.cs`)

```csharp
    [Fact]
    public async Task Nested_o2m_filter_narrows_list()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "O2MFilterCat" });
        await Post(c, "article", new { status = "published", categoryId = cat, translations = new { en = new { title = "Pub1" } } });
        await Post(c, "article", new { status = "draft", categoryId = cat, translations = new { en = new { title = "Drf1" } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = cat } },
            deep = new { articles = new { filter = new { status = new Dictionary<string, object> { ["_eq"] = "published" } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        row.GetProperty("articles").GetArrayLength().Should().Be(1); // draft filtered out
    }

    [Fact]
    public async Task Nested_m2m_filter_narrows_list()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var tagAi = await Post(c, "tag", new { name = "AI" });
        var tagUx = await Post(c, "tag", new { name = "UX" });
        var art = await Post(c, "article", new
        {
            status = "draft", tags = new[] { tagAi, tagUx },
            translations = new { en = new { title = "M2MFilterArt" } }
        });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = art } },
            deep = new { tags = new { filter = new { name = new Dictionary<string, object> { ["_eq"] = "AI" } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        var tags = row.GetProperty("tags");
        tags.GetArrayLength().Should().Be(1);
        tags[0].GetProperty("name").GetString().Should().Be("AI");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsExpansionTests.Nested_o2m_filter_narrows_list"`
Expected: FAIL — both articles/tags returned (filter not yet consumed).

- [ ] **Step 3: Implement filter push-down** — in `RelationExpander.ExpandAsync`, replace the O2M and M2M target-fetch calls.

O2M branch (`:81-102`), replace the `children` fetch:

```csharp
                case RelationKind.OneToMany:
                {
                    var ids = parents.Select(parentId).ToList();
                    var o2mFilter = spec.Filter is null ? null
                        : await filterResolver.RewriteAsync(rel.TargetCollection, spec.Filter, locale, ct);
                    var children = await repository.QueryWhereInFilteredAsync(
                        rel.TargetCollection, desc.ReverseForeignKeyProperty!, ids, o2mFilter, ct);
                    var grouped = children
                        .GroupBy(ch => readProp(ch, desc.ReverseForeignKeyProperty!)!)
                        .ToDictionary(g => g.Key, g => g.ToList());
                    foreach (var p in parents)
                    {
                        var pid = parentId(p);
                        var rows = new List<IReadOnlyDictionary<string, object?>>();
                        if (grouped.TryGetValue(pid, out var lst))
                            foreach (var ch in lst)
                            {
                                var d = (Dictionary<string, object?>)projectTarget(rel.TargetCollection, ch, spec.Fields);
                                rows.Add(d);
                                expanded.Add((ch, d));
                            }
                        result[pid][relName] = rows;
                    }
                    break;
                }
```

M2M branch (`:104-133`), replace the `targets` fetch:

```csharp
                case RelationKind.ManyToMany:
                {
                    var ids = parents.Select(parentId).ToList();
                    var junctions = await repository.QueryEntityWhereInAsync(
                        desc.JunctionType!, desc.JunctionParentFk!, ids, ct);
                    var targetIds = junctions
                        .Select(j => readProp(j, desc.JunctionTargetFk!)!)
                        .Distinct()
                        .ToList();
                    var m2mFilter = spec.Filter is null ? null
                        : await filterResolver.RewriteAsync(rel.TargetCollection, spec.Filter, locale, ct);
                    var targets = (await repository.QueryWhereInFilteredAsync(
                            rel.TargetCollection, "id", targetIds, m2mFilter, ct))
                        .ToDictionary(t => readProp(t, "id")!, t => t);
                    foreach (var p in parents)
                    {
                        var pid = parentId(p);
                        var rows = new List<IReadOnlyDictionary<string, object?>>();
                        var linked = junctions
                            .Where(j => Equals(readProp(j, desc.JunctionParentFk!), pid))
                            .OrderBy(j => JunctionSortKey(desc.JunctionSort, readProp, j))
                            .Select(j => readProp(j, desc.JunctionTargetFk!)!)
                            .Where(tid => targets.ContainsKey(tid));
                        foreach (var tid in linked)
                        {
                            var d = (Dictionary<string, object?>)projectTarget(rel.TargetCollection, targets[tid], spec.Fields);
                            rows.Add(d);
                            expanded.Add((targets[tid], d));
                        }
                        result[pid][relName] = rows;
                    }
                    break;
                }
```

(M2O branch unchanged: it has no filter — validation forbids args on M2O.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsExpansionTests"`
Expected: PASS (repo test + both filter tests).
Run: `dotnet test --filter "FullyQualifiedName~DeepNesting"` — Expected: PASS (no regression; unfiltered specs pass `null` filter = old behaviour).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Query/RelationExpander.cs tests/Struo.Tests/Query/NestedListArgsExpansionTests.cs
git commit -m "feat(query): push nested-list filter into batched expansion (8c.3b)"
```

---

### Task 7: Engine — consume `sort`/`limit`/`offset` (in-memory per group)

**Files:**
- Modify: `src/Struo.Infrastructure/Query/RelationExpander.cs` (add `ApplyListArgs` + comparer; wire into O2M + M2M loops)
- Test: `tests/Struo.Tests/Query/NestedListArgsExpansionTests.cs` (add tests)
- Modify: `tests/Struo.Tests/Query/DeepNestingBatchingTests.cs` (add N+1-with-args test)

**Interfaces:**
- Produces: nested lists ordered by `spec.Sort` (own-field, asc/desc, multi-key) and windowed by `spec.Offset`/`spec.Limit`, **per parent**; default order preserved when `Sort` is null.

- [ ] **Step 1: Write the failing tests** (append to `NestedListArgsExpansionTests.cs`)

```csharp
    [Fact]
    public async Task Nested_o2m_sort_and_limit_apply_per_parent()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "SortCat" });
        foreach (var s in new[] { "c", "a", "b" })
            await Post(c, "article", new { status = s, categoryId = cat, translations = new { en = new { title = "S-" + s } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = cat } },
            deep = new { articles = new { sort = new[] { "status" }, limit = 2 } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        var statuses = row.GetProperty("articles").EnumerateArray()
            .Select(a => a.GetProperty("status").GetString()).ToList();
        statuses.Should().Equal("a", "b"); // sorted asc, top-2
    }

    [Fact]
    public async Task Nested_o2m_offset_skips_per_parent()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "OffsetCat" });
        foreach (var s in new[] { "a", "b", "c" })
            await Post(c, "article", new { status = s, categoryId = cat, translations = new { en = new { title = "O-" + s } } });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = cat } },
            deep = new { articles = new { sort = new[] { "status" }, offset = 1, limit = 1 } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        var statuses = row.GetProperty("articles").EnumerateArray()
            .Select(a => a.GetProperty("status").GetString()).ToList();
        statuses.Should().Equal("b"); // skip 1 (a), take 1 -> b
    }

    [Fact]
    public async Task Nested_limit_is_independent_per_parent()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat1 = await Post(c, "category", new { name = "PP-1" });
        var cat2 = await Post(c, "category", new { name = "PP-2" });
        for (var i = 0; i < 3; i++)
            await Post(c, "article", new { status = "s" + i, categoryId = cat1, translations = new { en = new { title = $"PP1-{i}" } } });
        await Post(c, "article", new { status = "s0", categoryId = cat2, translations = new { en = new { title = "PP2-0" } } });

        // Query BOTH parents in one page; each must be trimmed independently to limit 2.
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { name = new Dictionary<string, object> { ["_starts_with"] = "PP-" } },
            sort = new[] { "name" },
            deep = new { articles = new { sort = new[] { "status" }, limit = 2 } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        data[0].GetProperty("articles").GetArrayLength().Should().Be(2); // PP-1: 3 -> capped at 2
        data[1].GetProperty("articles").GetArrayLength().Should().Be(1); // PP-2: 1 -> unchanged
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsExpansionTests.Nested_o2m_sort_and_limit_apply_per_parent"`
Expected: FAIL — all 3 articles returned in insertion order (sort/limit not consumed).

- [ ] **Step 3: Implement `ApplyListArgs` + comparer, wire into both loops**

Add these private members to `RelationExpander`:

```csharp
    /// <summary>
    /// Applies the nested-list <c>sort</c> (own-field, multi-key, asc/desc) then <c>offset</c>/<c>limit</c>
    /// to a single parent's group of target entities, in memory. When <c>Sort</c> is null the caller's
    /// existing order is preserved (O2M: fetch order; M2M: junction order). An omitted <c>Limit</c>
    /// returns all rows (8c.3a back-compat); an explicit <c>Limit</c> is clamped to
    /// <c>options.MaxLimit</c>. This is the per-parent windowing that keeps the batched fetch N+1-safe.
    /// Instance method: reads <c>options.MaxLimit</c> off the injected <see cref="StruoQueryOptions"/>.
    /// </summary>
    private IEnumerable<object> ApplyListArgs(
        List<object> entities, DeepRelationSpec spec, Func<object, string, object?> readProp)
    {
        IEnumerable<object> seq = entities;

        if (spec.Sort is { Count: > 0 } sorts)
        {
            IOrderedEnumerable<object>? ordered = null;
            foreach (var s in sorts)
            {
                var field = s.Field;
                Func<object, object?> key = e => readProp(e, field);
                ordered = ordered is null
                    ? (s.Descending
                        ? seq.OrderByDescending(key, RelationSortComparer.Instance)
                        : seq.OrderBy(key, RelationSortComparer.Instance))
                    : (s.Descending
                        ? ordered.ThenByDescending(key, RelationSortComparer.Instance)
                        : ordered.ThenBy(key, RelationSortComparer.Instance));
            }
            seq = ordered!;
        }

        var offset = spec.Offset.GetValueOrDefault();
        if (offset > 0) seq = seq.Skip(offset);
        if (spec.Limit is > 0) seq = seq.Take(Math.Min(spec.Limit.Value, options.MaxLimit));
        return seq;
    }

    /// <summary>
    /// Null-safe comparer for boxed own-field values (nulls sort first). Values on the same field
    /// share a CLR type, so <see cref="IComparable"/> ordering is well-defined; a non-comparable
    /// value degrades to equal (stable order preserved).
    /// </summary>
    private sealed class RelationSortComparer : IComparer<object?>
    {
        public static readonly RelationSortComparer Instance = new();
        public int Compare(object? x, object? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return x is IComparable c ? c.CompareTo(y) : 0;
        }
    }
```

Wire into the **O2M** loop — replace the inner `foreach (var ch in lst)` with a windowed loop:

```csharp
                        if (grouped.TryGetValue(pid, out var lst))
                            foreach (var ch in ApplyListArgs(lst, spec, readProp))
                            {
                                var d = (Dictionary<string, object?>)projectTarget(rel.TargetCollection, ch, spec.Fields);
                                rows.Add(d);
                                expanded.Add((ch, d));
                            }
```

Wire into the **M2M** loop — materialise the linked target entities, then window them:

```csharp
                        var linkedTargets = junctions
                            .Where(j => Equals(readProp(j, desc.JunctionParentFk!), pid))
                            .OrderBy(j => JunctionSortKey(desc.JunctionSort, readProp, j))
                            .Select(j => readProp(j, desc.JunctionTargetFk!)!)
                            .Where(tid => targets.ContainsKey(tid))
                            .Select(tid => targets[tid])
                            .ToList();
                        foreach (var t in ApplyListArgs(linkedTargets, spec, readProp))
                        {
                            var d = (Dictionary<string, object?>)projectTarget(rel.TargetCollection, t, spec.Fields);
                            rows.Add(d);
                            expanded.Add((t, d));
                        }
```

(Replace the previous M2M `linked` id-sequence + `foreach (var tid in linked)` block from Task 6 with this entity-based version.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsExpansionTests"`
Expected: PASS (all expansion tests).

- [ ] **Step 5: Add the N+1-with-args invariant test** (append to `DeepNestingBatchingTests.cs`)

```csharp
    private async Task<int> ExpandCountWithArgs(int articleCount, string label)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var catId = await Post(c, "category", new { name = $"ArgBatch-{label}" });
        for (var i = 0; i < articleCount; i++)
            await Post(c, "article", new
            {
                status = $"s{i}", categoryId = catId,
                translations = new { en = new { title = $"ArgBatch-{label}-{i}" } }
            });

        using var scope = _factory.Services.CreateScope();
        var real = scope.ServiceProvider.GetRequiredService<IItemRepository>();
        var graph = scope.ServiceProvider.GetRequiredService<RelationshipGraph>();
        var relFilter = scope.ServiceProvider.GetRequiredService<IRelationFilterResolver>();
        var opts = scope.ServiceProvider.GetRequiredService<StruoQueryOptions>();
        var counter = new CountingItemRepository(real);
        var expander = new RelationExpander(counter, graph, relFilter, opts);

        var parents = await real.QueryWhereInAsync("category", "id", new object[] { System.Guid.Parse(catId) });

        // articles(O2M) with filter+sort+limit, nested category(M2O). Args must NOT add queries.
        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>
        {
            ["articles"] = new DeepRelationSpec(null, 1,
                new DeepSpec(new Dictionary<string, DeepRelationSpec> { ["category"] = new DeepRelationSpec(null, null) }))
            {
                Filter = new ComparisonFilter("status", QueryOperator.Neq, "zzz"),
                Sort = new List<SortField> { new("status", false) }
            }
        });

        counter.ResetCount();
        await expander.ExpandAsync(
            "category", parents, deep,
            projectTarget: (_, entity, _) => new Dictionary<string, object?> { ["id"] = ReadProp(entity, "id") },
            parentId: entity => ReadProp(entity, "id")!,
            readProp: ReadProp);
        return counter.WhereInCalls;
    }

    [Fact]
    public async Task Query_count_with_args_is_constant_in_row_count()
    {
        var few = await ExpandCountWithArgs(2, "few");
        var many = await ExpandCountWithArgs(8, "many");
        few.Should().Be(many);
        many.Should().Be(2); // 1 for articles + 1 for nested category, regardless of args or row count
    }
```

(Add `using Struo.Application.Query;` / `using Struo.Domain.Query;` to the test file if not present — `ComparisonFilter`, `SortField`, `QueryOperator`, `IRelationFilterResolver`.)

- [ ] **Step 6: Run the invariant test**

Run: `dotnet test --filter "FullyQualifiedName~DeepNestingBatchingTests"`
Expected: PASS (both the original count test and the with-args one; count == 2).

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Infrastructure/Query/RelationExpander.cs tests/Struo.Tests/Query/NestedListArgsExpansionTests.cs tests/Struo.Tests/Query/DeepNestingBatchingTests.cs
git commit -m "feat(query): apply nested-list sort/limit/offset per parent in-memory (8c.3b)"
```

---

### Task 8: Engine — cross-relation nested filter + trim-before-recurse

**Files:**
- Test only: `tests/Struo.Tests/Query/NestedListArgsExpansionTests.cs`

**Interfaces:**
- Consumes: the Task 6 filter push-down (which already routes `spec.Filter` through `RelationFilterResolver.RewriteAsync`, so dotted cross-relation paths resolve). This task adds coverage — no new production code expected. If a test fails, fix in `RelationExpander`/`ItemService` and note it.

- [ ] **Step 1: Write the tests** (append)

```csharp
    [Fact]
    public async Task Nested_cross_relation_filter_resolves()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var wanted = await Post(c, "category", new { name = "WantedCat" });
        var other = await Post(c, "category", new { name = "OtherCat" });
        var tag = await Post(c, "tag", new { name = "Shared" });
        // article in WantedCat with the tag; article in OtherCat with the tag.
        await Post(c, "article", new { status = "draft", categoryId = wanted, tags = new[] { tag }, translations = new { en = new { title = "W" } } });
        await Post(c, "article", new { status = "draft", categoryId = other, tags = new[] { tag }, translations = new { en = new { title = "O" } } });

        // Expand the tag's articles, filtered by a CROSS-RELATION path (article.category.name).
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = tag } },
            deep = new { articles = new { filter = new { category = new { name = new Dictionary<string, object> { ["_eq"] = "WantedCat" } } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/tag/query", envelope);
        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        row.GetProperty("articles").GetArrayLength().Should().Be(1); // only the WantedCat article
    }

    [Fact]
    public async Task Limit_trims_before_recursing_into_nested_deep()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "TrimCat" });
        foreach (var s in new[] { "a", "b", "c" })
            await Post(c, "article", new { status = s, categoryId = cat, translations = new { en = new { title = "T-" + s } } });

        // limit=1 on articles; each surviving article expands its category (nested M2O).
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = cat } },
            deep = new { articles = new { sort = new[] { "status" }, limit = 1, deep = new { category = new { } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        var articles = row.GetProperty("articles");
        articles.GetArrayLength().Should().Be(1);
        articles[0].GetProperty("status").GetString().Should().Be("a");
        articles[0].GetProperty("category").GetProperty("name").GetString().Should().Be("TrimCat");
    }
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsExpansionTests.Nested_cross_relation_filter_resolves"`
Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsExpansionTests.Limit_trims_before_recursing_into_nested_deep"`
Expected: PASS (cross-relation via existing `RewriteAsync`; trim-before-recurse because `expanded` only holds the windowed survivors).

If either fails: the cross-relation case implies `RewriteAsync` needs the resolved dotted path — confirm `spec.Filter` is passed through `RewriteAsync` (Task 6). The trim case implies the recursion at `RelationExpander.cs:141-149` runs over `expanded`; since Task 7 only adds windowed entities to `expanded`, it should already trim. Debug per superpowers:systematic-debugging if needed.

- [ ] **Step 3: Commit**

```bash
git add tests/Struo.Tests/Query/NestedListArgsExpansionTests.cs
git commit -m "test(query): cross-relation nested filter + trim-before-recurse (8c.3b)"
```

---

### Task 9: GraphQL — declare args on O2M/M2M relation fields

**Files:**
- Modify: `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs:106-112` (relation-field loop)
- Create: `tests/Struo.Tests/GraphQl/NestedListArgsSchemaTests.cs`

**Interfaces:**
- Consumes: `SchemaTypeMapper.TypeName(targetCollection)`; the per-collection `{Target}FilterInput` type already built for the root/8c.2 surface; the `Field(name, sdl, resolver)` helper + `ArgumentConfiguration` (as used in `CollectionResolvers.ListField`).
- Produces: each O2M/M2M relation field on object type `X` carries `filter: {Target}FilterInput`, `sort: [String!]`, `limit: Int`, `offset: Int`. M2O relation fields carry no arguments.

- [ ] **Step 1: Write the failing test** (`NestedListArgsSchemaTests.cs`)

Mirror the existing `GraphQlSchemaTests` schema-printing pattern (it prints the schema via `schema.ToString()` / SDL and asserts on it). Confirm the exact schema-build entry point from `GraphQlSchemaTests.cs` and reuse it.

```csharp
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

[Collection("ApiIntegration")]
public class NestedListArgsSchemaTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task ToMany_relation_fields_expose_list_args_and_m2o_does_not()
    {
        var sdl = await GraphQlSchemaTestHelper.PrintSdlAsync(_factory); // reuse the helper GraphQlSchemaTests uses

        // Article.tags is M2M -> has args; Article.category is M2O -> no args.
        // Assert on the Article type block of the SDL.
        sdl.Should().MatchRegex(@"tags\s*\([^)]*filter:\s*TagFilterInput[^)]*limit:\s*Int[^)]*\)");
        sdl.Should().MatchRegex(@"category:\s*Category(\s|$)"); // no argument list on the M2O field
    }
}
```

Note: if `GraphQlSchemaTests` does not expose a reusable helper, inline its schema-build code here (copy the `ApiFactory` → schema resolution it uses; do not invent a new API). Adjust the regex to the actual SDL formatting (run the test once to print `sdl` and calibrate). The **behavioural** assertion is: `filter`/`sort`/`limit`/`offset` appear on `tags` (M2M) and on `articles`/`children` (O2M) but not on `category`/`parent` (M2O).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsSchemaTests"`
Expected: FAIL — relation list fields currently have no arguments.

- [ ] **Step 3: Implement** — in `CollectionSchemaBuilder.BuildObjectType`, replace the relation-field loop (`:106-112`):

```csharp
        // relations (single-level value pre-nested by ItemService deep expansion).
        // To-many (O2M/M2M) list fields gain nested-list args (8c.3b); M2O stays a bare object field.
        foreach (var rel in meta.Relations)
        {
            var target = SchemaTypeMapper.TypeName(rel.TargetCollection);
            if (rel.Kind == RelationKind.ManyToOne)
            {
                config.Fields.Add(Field(rel.Name, target, ctx => ParentDict(ctx).GetValueOrDefault(rel.Name)));
            }
            else
            {
                var field = Field(rel.Name, $"[{target}!]", ctx => ParentDict(ctx).GetValueOrDefault(rel.Name));
                field.Arguments.Add(new ArgumentConfiguration("filter", null, TypeReference.Parse($"{target}FilterInput")));
                field.Arguments.Add(new ArgumentConfiguration("sort", null, TypeReference.Parse("[String!]")));
                field.Arguments.Add(new ArgumentConfiguration("limit", null, TypeReference.Parse("Int")));
                field.Arguments.Add(new ArgumentConfiguration("offset", null, TypeReference.Parse("Int")));
                config.Fields.Add(field);
            }
        }
```

Confirm the `Field(...)` helper returns an `ObjectFieldConfiguration` exposing `.Arguments` (same type used in `CollectionResolvers.ListField`). Add `using HotChocolate.Types.Descriptors;` / the `ArgumentConfiguration`/`TypeReference` usings already present in `CollectionResolvers.cs` if the builder lacks them. Ensure `RelationKind` is imported (`Struo.Domain.Metadata.Enums`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsSchemaTests"`
Expected: PASS.
Run: `dotnet test --filter "FullyQualifiedName~GraphQlSchemaTests"` — Expected: PASS (no regression to the existing schema shape).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs tests/Struo.Tests/GraphQl/NestedListArgsSchemaTests.cs
git commit -m "feat(graphql): declare filter/sort/limit/offset args on to-many relation fields (8c.3b)"
```

---

### Task 10: GraphQL — read selection args into `DeepRelationSpec`

**Files:**
- Modify: `src/Struo.Api/GraphQl/CollectionResolvers.cs:107-124` (`BuildDeep`)
- Create: `tests/Struo.Tests/GraphQl/NestedListArgsExecutionTests.cs`

**Interfaces:**
- Consumes: `ISelection sel` for each child relation selection (from `ctx.GetSelections(...)`); `FilterInputTranslator.Translate(dict, targetCollection, RelationTargets(metadata))`; `GraphQlQueryBuilder.ParseSort(tokens)`.
- Produces: `BuildDeep` emits `DeepRelationSpec` populated with `Filter`/`Sort`/`Limit`/`Offset` read from the selection's arguments.

**⚠ Genuinely-new HC surface (spec §15):** reading a *sub-selection's* argument values. The implementation below uses `sel.Arguments` (HC v16 `ArgumentMap : IReadOnlyDictionary<string, ArgumentValue>`, each `ArgumentValue.Value` = coerced runtime value). Confirm with BOTH the inline-literal and `$variable` execution tests below. If the `$variable` case yields a null `.Value`, the args are not fully coerced on the compiled selection — coerce against `ctx` inside the helper (isolate the fix there; do not spread HC calls through `BuildDeep`). This mirrors the 8b.1 `SentFieldsOnly` approach of reading argument state off HC internals behind one helper.

- [ ] **Step 1: Write the failing tests** (`NestedListArgsExecutionTests.cs`)

Mirror `GraphQlExecutionTests` (it builds the schema + executes an operation and reads the JSON result). Reuse that harness (copy its executor setup if there is no shared helper).

```csharp
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

[Collection("ApiIntegration")]
public class NestedListArgsExecutionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Nested_list_filter_and_limit_via_inline_literal()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        // seed a category with published + draft articles via REST (reuse the REST create path).
        var cat = await GraphQlTestSeed.Category(c, "GqlArgsCat");
        await GraphQlTestSeed.Article(c, cat, "published", "GA-pub");
        await GraphQlTestSeed.Article(c, cat, "draft", "GA-drf");

        var query = $$"""
        query { category(id: "{{cat}}") {
            articles(filter: { status: { eq: "published" } }, limit: 5) { status }
        } }
        """;
        var json = await GraphQlTestExec.PostAsync(c, query);
        var articles = json.RootElement.GetProperty("data").GetProperty("category").GetProperty("articles");
        articles.GetArrayLength().Should().Be(1);
        articles[0].GetProperty("status").GetString().Should().Be("published");
    }

    [Fact]
    public async Task Nested_list_filter_via_variable()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await GraphQlTestSeed.Category(c, "GqlVarCat");
        await GraphQlTestSeed.Article(c, cat, "published", "GV-pub");
        await GraphQlTestSeed.Article(c, cat, "draft", "GV-drf");

        var query = $$"""
        query($f: ArticleFilterInput) { category(id: "{{cat}}") {
            articles(filter: $f) { status }
        } }
        """;
        var variables = new { f = new { status = new { eq = "published" } } };
        var json = await GraphQlTestExec.PostAsync(c, query, variables);
        var articles = json.RootElement.GetProperty("data").GetProperty("category").GetProperty("articles");
        articles.GetArrayLength().Should().Be(1);
    }
}
```

Note: `GraphQlTestSeed` / `GraphQlTestExec` are shorthand for whatever seed + `/graphql` POST helpers the existing GraphQL tests use — reuse the real ones from `GraphQlExecutionTests`/`GraphQlEndpointTests` (POST JSON `{query, variables}` to `/graphql`, parse the response). Do not invent new infrastructure; wire to the existing pattern. Both tests must be driven through the real `/graphql` HTTP endpoint so the full pipeline (including variable coercion) is exercised.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsExecutionTests"`
Expected: FAIL — both articles returned (args not read into the spec yet).

- [ ] **Step 3: Implement** — replace `BuildDeep` in `CollectionResolvers.cs`:

```csharp
    private static DeepSpec? BuildDeep(
        IResolverContext ctx, string collection, SelectionEnumerator childSelections, IMetadataProvider metadata)
    {
        var relByName = metadata.GetCollection(collection)?.Relations
            .ToDictionary(r => r.Name, r => r, StringComparer.OrdinalIgnoreCase);
        if (relByName is null || relByName.Count == 0) return null;

        var map = new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var sel in childSelections)
        {
            if (!relByName.TryGetValue(sel.Field.Name, out var rel)) continue;
            if (map.ContainsKey(rel.Name)) continue; // aliased-duplicate selections: first wins per level
            var targetType = (ObjectType)sel.Field.Type.NamedType();
            var nested = BuildDeep(ctx, rel.TargetCollection, ctx.GetSelections(targetType, sel), metadata);

            // To-many list fields may carry filter/sort/limit/offset arguments (8c.3b).
            FilterNode? filter = null;
            IReadOnlyList<SortField>? sort = null;
            int? limit = null, offset = null;
            if (rel.Kind is RelationKind.OneToMany or RelationKind.ManyToMany)
            {
                var args = sel.Arguments;
                if (args.TryGetValue("filter", out var fv) && fv.Value is IReadOnlyDictionary<string, object?> fd)
                    filter = FilterInputTranslator.Translate(fd, rel.TargetCollection, RelationTargets(metadata));
                if (args.TryGetValue("sort", out var sv) && sv.Value is IEnumerable<object?> st)
                    sort = GraphQlQueryBuilder.ParseSort(st.Select(x => x?.ToString() ?? "").ToList());
                if (args.TryGetValue("limit", out var lv) && lv.Value is not null)
                    limit = Convert.ToInt32(lv.Value);
                if (args.TryGetValue("offset", out var ov) && ov.Value is not null)
                    offset = Convert.ToInt32(ov.Value);
            }

            map[rel.Name] = new DeepRelationSpec(null, limit, nested)
            {
                Filter = filter,
                Sort = sort,
                Offset = offset
            };
        }
        return map.Count == 0 ? null : new DeepSpec(map);
    }
```

Add usings: `Struo.Domain.Query` (for `FilterNode`/`SortField`) is already imported. Confirm `ISelection.Arguments` shape against HC 16.4.0 (see the ⚠ note). If `sort`'s `.Value` is a `List<string>` already, the `.Select(...ToString())` still works.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NestedListArgsExecutionTests"`
Expected: PASS (both inline literal and variable).

If the variable test fails with both rows returned (filter null), the compiled selection's `ArgumentValue.Value` is not coerced for variables. Fix inside `BuildDeep` (or a small `ReadSelectionArgs(ctx, sel)` helper): coerce the argument literal against `ctx.Variables` before reading. Keep the change isolated behind the helper and re-run.

- [ ] **Step 5: Run the whole GraphQL suite + full build**

Run: `dotnet test --filter "FullyQualifiedName~GraphQl"` — Expected: PASS (no regression, esp. `GraphQlExecutionTests` nested-expansion + aliased-duplicate).
Run: `dotnet build -warnaserror` — Expected: 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Api/GraphQl/CollectionResolvers.cs tests/Struo.Tests/GraphQl/NestedListArgsExecutionTests.cs
git commit -m "feat(graphql): read nested-list args from selection into DeepRelationSpec (8c.3b)"
```

---

### Task 11: Full regression + docs

**Files:**
- Modify: `docs/ROADMAP.md` (8c.3b status + verification baseline; flip the 8c.3b table row to done-pending-live)
- Modify: `docs/guide/*` (nested-list args usage: GraphQL args + REST envelope; per-parent limit/offset; own-field-sort-only; M2O-no-args; omitted-limit-returns-all)

**Interfaces:** none (docs + gate).

- [ ] **Step 1: Run the full backend suite**

Run: `dotnet build -warnaserror` then `dotnet test`
Expected: 0 warnings; all green. Record the new count (baseline **549** + the new tests from Tasks 1-10). Confirm 0 failed / 0 skipped.

- [ ] **Step 2: Update `docs/ROADMAP.md`**

Add a Phase 8c.3b bullet in the "Status at a glance" section mirroring the 8c.3a entry's structure (scope, layers touched, execution strategy A, N+1 invariant, back-compat, deferred items), a post-8c.3b verification baseline line with the new test count, and flip the 8c.3b table row from `⬜ planned` to `✅ done (pending live gate)` with spec/plan links. Note the live gate is user-run next (per project convention).

- [ ] **Step 3: Update the guide**

In the deep-expansion guide section, add: nested-list `filter`/`sort`/`limit`/`offset` examples for GraphQL (`tags(filter:{...}, sort:["name"], limit:5)`) and the REST envelope (`deep:{"tags":{"filter":{...},"sort":["name"],"limit":5}}`); state that these apply to to-many relations only (M2O has no args), sort is own-field only, `limit`/`offset` are per-parent, omitting `limit` returns all rows, and an explicit `limit` is clamped to `MaxLimit`.

- [ ] **Step 4: Commit**

```bash
git add docs/ROADMAP.md docs/guide/
git commit -m "docs(roadmap,guide): Phase 8c.3b nested-list args done (pending live gate)"
```

- [ ] **Step 5: Final whole-branch review**

Request a code review (superpowers:requesting-code-review or the project's Opus review flow). Address Critical/Important findings. Then hand off to the user for the live Postgres gate (§12 of the spec: nested filter discrimination, independent per-parent limit/offset, CJK code-point-exact, M2O-args + bad-path `BAD_USER_INPUT`, arg-less back-compat).

---

## Self-Review

**1. Spec coverage:**
- §3.1 args on O2M/M2M both surfaces → Tasks 2 (REST parse), 9-10 (GraphQL). ✅
- §3.2 filter = cross-relation dotted-path → Tasks 6, 8 (via `RewriteAsync`). ✅
- §3.3 sort own-field multi-key → Task 7 + validation Task 3. ✅
- §3.4 limit/offset per-parent + `MaxLimit` clamp → Task 7 `ApplyListArgs` (`Math.Min(spec.Limit.Value, options.MaxLimit)`); `StruoQueryOptions` injected into `RelationExpander` in Task 5 so no late constructor churn. ✅
- §7 validation (to-many only, whitelist, own-field sort, bounds) → Task 3. ✅
- §8.1 N+1 invariant with args → Task 7 count test. ✅
- Back-compat omitted-limit-all → covered by unchanged null-spec path + existing DeepNesting tests staying green (Tasks 5-7). ✅
- Docs → Task 11. ✅

**2. Placeholder scan:** The GraphQL sub-selection arg API (Task 10) is a documented spike with concrete acceptance tests, not a placeholder. The schema/exec test helpers (Tasks 9-10) say "reuse the existing harness" — acceptable (the real helpers exist; inventing parallel ones would be wrong). No "TODO"/"TBD"/"handle edge cases" left.

**3. Type consistency:** `QueryWhereInFilteredAsync` signature identical across interface + 3 impls (`SqlSugarItemRepository`, `CountingItemRepository`, `StubRepo`) + expander calls. `DeepRelationSpec` positional `(Fields, Limit, Deep)` + init `Filter`/`Sort`/`Offset` used consistently. `ExpandAsync` `locale` param added to interface + impl + all call sites (ItemService ×2, both batching-test helpers). `RelationExpander` constructor gains `IRelationFilterResolver` **and** `StruoQueryOptions` in Task 5; every direct construction (`DeepNestingBatchingTests` ×2 helpers) passes both — verified in Task 5 Step 4 and Task 7 Step 5. `ApplyListArgs` is an instance method (reads `options.MaxLimit`), called with 3 args from both O2M/M2M loops.
