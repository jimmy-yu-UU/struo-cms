# Phase 8c.3a — Multi-level (depth > 1) relation nesting/expansion — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make relation expansion recurse to arbitrary depth (up to `MaxRelationDepth`) on both the GraphQL read API and the REST `deep` JSON envelope, so a relation-of-a-relation (`article.category.parent`, `category.children.articles`, self-referential cycles) resolves instead of returning empty.

**Architecture:** A shared-engine change. `DeepRelationSpec` gains a recursive nested `DeepSpec? Deep`. `RelationExpander.ExpandAsync` recurses **breadth-first per level** (one batched follow-up query per relation-node, independent of row count — N+1-safe). `ItemService.ExpandDeepAsync` validates **nesting depth** (not relation count) and per-level relation names. GraphQL builds the nested `DeepSpec` from the selection tree; the REST JSON envelope parser recurses into a nested `deep` key. No GraphQL schema-shape change (relation fields already permit arbitrary-depth selection; Phase 8 only truncated the resolution).

**Tech Stack:** .NET 10 / C# · SqlSugarCore · HotChocolate v16 (GraphQL) · xUnit + AwesomeAssertions · SQLite (tests) / PostgreSQL (live gate). Design spec: [`docs/superpowers/specs/2026-07-13-phase8c3a-multilevel-relation-nesting-design.md`](../specs/2026-07-13-phase8c3a-multilevel-relation-nesting-design.md).

## Global Constraints

- **Dependency rule (§2):** Domain → nothing · Application → Domain · Infrastructure → Application+Domain · Api → Application+Infrastructure. Domain stays package-free.
- **§17.4:** All DB access via SqlSugar ORM; the expander deliberately does **not** use `.Includes()` (relation rows go through the metadata projection delegate).
- **§17.5:** No package versions authored by hand; **no new NuGet packages are added by this plan**. `Directory.Packages.props` unchanged.
- **No `samples/*` change** — the sample `Struo.Sample.Blog` already has Article↔Category (M2O + O2M), Article↔Tag (M2M), and Category self-reference (`parent`/`children`), which cover every depth>1 shape.
- **Build gate:** `dotnet build -warnaserror` must stay clean (0 warnings). Backend test baseline before this plan: **534**. Frontend untouched: **237**.
- **`MaxRelationDepth`** default = **5** (`StruoQueryOptions.MaxRelationDepth`). HotChocolate max-execution-depth = **12** (unchanged; comfortably accommodates 5 relation hops).
- **`Deep` defaults to `null`** on `DeepRelationSpec` so every existing construction site compiles unchanged and keeps depth-1 semantics.
- Run tests from the repo root: `dotnet test tests/Struo.Tests/Struo.Tests.csproj`. Filter a single test with `--filter "FullyQualifiedName~<TestName>"`.

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/Struo.Domain/Query/DeepSpec.cs` | Add recursive `DeepSpec? Deep` to `DeepRelationSpec` | 1 |
| `src/Struo.Application/Query/QueryParser.cs` | Recursive REST `deep` JSON-envelope parse | 2 |
| `src/Struo.Application/Query/ItemService.cs` | Recursive depth + per-level name validation | 3 |
| `src/Struo.Infrastructure/Query/RelationExpander.cs` | Recursive batched expansion + merge | 4 |
| `tests/Struo.Tests/Support/CountingItemRepository.cs` | Counting decorator for the N+1 invariant test | 5 |
| `src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs` | `BuildQuery` takes `DeepSpec?` | 6 |
| `src/Struo.Api/GraphQl/CollectionResolvers.cs` | Recursive `SelectionDeepSpec` from the selection tree | 6 |
| `docs/ROADMAP.md`, `docs/guide/*` | Status + usage docs | 7 |

Tests live in `tests/Struo.Tests/{Query,GraphQl}/`. Integration tests drive the real stack over HTTP via `ApiFactory` (SQLite), following `DeepExpansionTests` / `CrossRelationFilterTests`.

---

## Task 1: Domain — recursive `DeepRelationSpec`

**Files:**
- Modify: `src/Struo.Domain/Query/DeepSpec.cs`
- Test: `tests/Struo.Tests/Query/DeepSpecTests.cs` (create)

**Interfaces:**
- Produces: `record DeepRelationSpec(IReadOnlyList<string>? Fields, int? Limit, DeepSpec? Deep = null)` — the `Deep` property is consumed by Tasks 2, 3, 4, 6. `DeepSpec(IReadOnlyDictionary<string, DeepRelationSpec> Relations)` unchanged.

- [ ] **Step 1: Write the failing test**

Create `tests/Struo.Tests/Query/DeepSpecTests.cs`:

```csharp
using AwesomeAssertions;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class DeepSpecTests
{
    [Fact]
    public void DeepRelationSpec_defaults_Deep_to_null()
    {
        // Existing depth-1 construction sites keep compiling and stay depth-1.
        var spec = new DeepRelationSpec(null, null);
        spec.Deep.Should().BeNull();
    }

    [Fact]
    public void DeepRelationSpec_carries_a_nested_DeepSpec()
    {
        var nested = new DeepSpec(new Dictionary<string, DeepRelationSpec>
        {
            ["parent"] = new DeepRelationSpec(null, null)
        });
        var spec = new DeepRelationSpec(Fields: null, Limit: null, Deep: nested);

        spec.Deep.Should().BeSameAs(nested);
        spec.Deep!.Relations.Should().ContainKey("parent");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DeepSpecTests"`
Expected: FAIL to **compile** — `DeepRelationSpec` has no `Deep` member / no 3-arg constructor.

- [ ] **Step 3: Write minimal implementation**

Replace the body of `src/Struo.Domain/Query/DeepSpec.cs` with:

```csharp
// src/Struo.Domain/Query/DeepSpec.cs
namespace Struo.Domain.Query;

/// <summary>
/// Per-relation deep-expansion options: an optional field whitelist for the expanded target
/// rows, an optional limit (reserved — consumed by 8c.3b nested-list args), and an optional
/// nested <see cref="DeepSpec"/> for multi-level (depth &gt; 1) expansion of the target's own
/// relations. <c>Deep</c> defaults to null so depth-1 call sites are unchanged.
/// </summary>
public sealed record DeepRelationSpec(
    IReadOnlyList<string>? Fields, int? Limit, DeepSpec? Deep = null);

/// <summary>
/// Read-time relation-expansion request: maps each requested relation name to its
/// <see cref="DeepRelationSpec"/>. Lookups are case-insensitive. Recursion rides on the
/// per-relation <see cref="DeepRelationSpec.Deep"/>.
/// </summary>
public sealed record DeepSpec(IReadOnlyDictionary<string, DeepRelationSpec> Relations);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DeepSpecTests"`
Expected: PASS (2 tests). Also run a full build to confirm no existing call site broke:
Run: `dotnet build -warnaserror`
Expected: 0 warnings, 0 errors (all `new DeepRelationSpec(x, y)` sites still valid via the default).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Domain/Query/DeepSpec.cs tests/Struo.Tests/Query/DeepSpecTests.cs
git commit -m "feat(query): DeepRelationSpec gains recursive nested DeepSpec (8c.3a)"
```

---

## Task 2: Application — recursive REST `deep` JSON-envelope parse

**Files:**
- Modify: `src/Struo.Application/Query/QueryParser.cs` (`ParseDeepEnvelope`, lines ~45-62)
- Test: `tests/Struo.Tests/Query/QueryParserTests.cs` (add tests; follow existing style in that file)

**Interfaces:**
- Consumes: `DeepRelationSpec(..., DeepSpec? Deep)` from Task 1.
- Produces: `QueryParser.ParseEnvelope(JsonElement)` now returns a `QueryModel` whose `Deep` tree nests when the envelope nests. The query-string form (`ParseDeepQueryString`) is **unchanged** (stays flat).

- [ ] **Step 1: Write the failing test**

Add to `tests/Struo.Tests/Query/QueryParserTests.cs` (matching its existing `using`s — `System.Text.Json`, `AwesomeAssertions`, `Struo.Application.Query`, `Struo.Domain.Query`, `Xunit`):

```csharp
[Fact]
public void ParseEnvelope_parses_nested_deep()
{
    var env = JsonSerializer.SerializeToElement(new
    {
        deep = new { category = new { deep = new { parent = new { } } } }
    });

    var model = QueryParser.ParseEnvelope(env);

    model.Deep.Should().NotBeNull();
    var category = model.Deep!.Relations["category"];
    category.Deep.Should().NotBeNull();
    category.Deep!.Relations.Should().ContainKey("parent");
    category.Deep.Relations["parent"].Deep.Should().BeNull(); // leaf
}

[Fact]
public void ParseEnvelope_keeps_flat_deep_backward_compatible()
{
    var env = JsonSerializer.SerializeToElement(new
    {
        deep = new { category = new { fields = new[] { "name" } } }
    });

    var model = QueryParser.ParseEnvelope(env);

    var category = model.Deep!.Relations["category"];
    category.Fields.Should().ContainSingle().Which.Should().Be("name");
    category.Deep.Should().BeNull();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~QueryParserTests.ParseEnvelope_parses_nested_deep"`
Expected: FAIL — `category.Deep` is null (the parser does not read a nested `deep` key yet).

- [ ] **Step 3: Write minimal implementation**

In `src/Struo.Application/Query/QueryParser.cs`, replace `ParseDeepEnvelope` (lines ~45-62) with a recursive pair:

```csharp
    private static DeepSpec? ParseDeepEnvelope(JsonElement env) =>
        env.TryGetProperty("deep", out var d) && d.ValueKind == JsonValueKind.Object
            ? ParseDeepObject(d)
            : null;

    private static DeepSpec? ParseDeepObject(JsonElement d)
    {
        var map = new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in d.EnumerateObject())
        {
            IReadOnlyList<string>? fields = null;
            int? limit = null;
            DeepSpec? nested = null;
            if (rel.Value.ValueKind == JsonValueKind.Object)
            {
                if (rel.Value.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array)
                    fields = f.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                if (rel.Value.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li)) limit = li;
                if (rel.Value.TryGetProperty("deep", out var nd) && nd.ValueKind == JsonValueKind.Object)
                    nested = ParseDeepObject(nd);
            }
            map[rel.Name] = new DeepRelationSpec(fields, limit, nested);
        }
        return map.Count == 0 ? null : new DeepSpec(map);
    }
```

(The query-string parser `ParseDeepQueryString` is left exactly as-is — no nesting on the flat form.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~QueryParserTests"`
Expected: PASS (new tests + existing QueryParser tests stay green as characterization).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/QueryParser.cs tests/Struo.Tests/Query/QueryParserTests.cs
git commit -m "feat(query): REST deep JSON envelope parses nested deep (8c.3a)"
```

---

## Task 3: Application — recursive depth + per-level name validation

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs` (`ExpandDeepAsync`, lines ~208-224; add a private `ValidateDeepTree`)
- Test: `tests/Struo.Tests/Query/DeepNestingValidationTests.cs` (create; HTTP integration via `ApiFactory`)

**Interfaces:**
- Consumes: `DeepSpec` tree (Tasks 1–2); `graph.Resolve(string, string)` returns `RelationMetadata?` with `.TargetCollection`; `options.MaxRelationDepth` (an `ItemService` ctor dependency, already present).
- Produces: `ExpandDeepAsync` throws `QueryException` (→ HTTP 400 / GraphQL `BAD_USER_INPUT`) when nesting depth > `MaxRelationDepth` or any relation name is unknown at its level. Behaviour change: the old **relation-count** cap is removed.

**Context:** The old head of `ExpandDeepAsync` (lines ~213-224) is:

```csharp
        if (deep is null || deep.Relations.Count == 0 || entities.Count == 0) return;

        // Validate: deep expansion is single-level here, so MaxRelationDepth caps the number
        // of relations expanded in one request (each adds one level of nesting / one batch query).
        if (deep.Relations.Count > options.MaxRelationDepth)
            throw new QueryException(
                $"Too many deep relations requested ({deep.Relations.Count}); the maximum is {options.MaxRelationDepth}.");
        foreach (var relName in deep.Relations.Keys)
        {
            if (graph.Resolve(collection, relName) is null)
                throw new QueryException($"Unknown relation '{relName}' on '{collection}'.");
        }
```

No existing test asserts the old count-cap message (verified: no `tests/**` reference to "Too many deep" / `MaxRelationDepth` / `Relations.Count`). If the executor finds one, update it to the new depth semantics.

- [ ] **Step 1: Write the failing test**

Create `tests/Struo.Tests/Query/DeepNestingValidationTests.cs`:

```csharp
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
        // MaxRelationDepth = 5 => a 6-level nested chain must be rejected before any query runs.
        var envelope = JsonSerializer.SerializeToElement(new { deep = NestParent(6) });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_nested_relation_returns_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var envelope = JsonSerializer.SerializeToElement(new
        {
            deep = new { category = new { deep = new { ghostrel = new { } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Many_sibling_relations_at_depth_one_are_allowed()
    {
        // The old cap rejected > MaxRelationDepth *relations* regardless of nesting.
        // article has category + tags (2 siblings) at depth 1 — must be OK now.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var envelope = JsonSerializer.SerializeToElement(new
        {
            deep = new { category = new { }, tags = new { } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DeepNestingValidationTests"`
Expected: FAIL — `Nested_deep_over_max_depth_returns_400` returns 200 (old count-cap sees 1 top-level relation, passes it; the engine ignores the deeper nesting).

- [ ] **Step 3: Write minimal implementation**

In `src/Struo.Application/Query/ItemService.cs`, replace the validation block (the `if (deep.Relations.Count > ...)` cap and the flat `foreach` name check) with a single call, keeping the early-return line:

```csharp
        if (deep is null || deep.Relations.Count == 0 || entities.Count == 0) return;

        // Validate the whole nested tree: nesting depth <= MaxRelationDepth, and every relation
        // name resolves against its own level's collection. Runs before any query executes.
        ValidateDeepTree(collection, deep, depth: 1);
```

Add this private method to `ItemService` (near `ExpandDeepAsync`; `graph` and `options` are existing fields):

```csharp
    /// <summary>
    /// Recursively validates a <see cref="DeepSpec"/> tree: throws if nesting depth exceeds
    /// <see cref="StruoQueryOptions.MaxRelationDepth"/> or a relation name is unknown at its level.
    /// </summary>
    private void ValidateDeepTree(string coll, DeepSpec spec, int depth)
    {
        if (depth > options.MaxRelationDepth)
            throw new QueryException(
                $"Relation nesting too deep (depth {depth}); the maximum is {options.MaxRelationDepth}.");
        foreach (var (relName, relSpec) in spec.Relations)
        {
            var rel = graph.Resolve(coll, relName)
                ?? throw new QueryException($"Unknown relation '{relName}' on '{coll}'.");
            if (relSpec.Deep is not null)
                ValidateDeepTree(rel.TargetCollection, relSpec.Deep, depth + 1);
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DeepNestingValidationTests"`
Expected: PASS (3 tests). Then confirm the existing depth-1 suite is still green:
Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DeepExpansionTests"`
Expected: PASS (depth-1 behaviour unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/DeepNestingValidationTests.cs
git commit -m "feat(query): validate deep nesting depth + per-level relation names (8c.3a)"
```

---

## Task 4: Infrastructure — recursive batched expansion

**Files:**
- Modify: `src/Struo.Infrastructure/Query/RelationExpander.cs` (`ExpandAsync`)
- Test: `tests/Struo.Tests/Query/DeepNestingExpansionTests.cs` (create; HTTP integration via `ApiFactory`)

**Interfaces:**
- Consumes: `DeepRelationSpec.Deep` (Task 1); existing `IItemRepository.QueryWhereInAsync` / `QueryEntityWhereInAsync`; `RelationshipGraph.Descriptors`; the `projectTarget` / `parentId` / `readProp` delegates supplied by `ItemService` (collection-agnostic; every entity's id property is `Id`).
- Produces: `ExpandAsync` recurses so a relation whose `spec.Deep` is non-null has its target rows' own relations expanded and merged, batched **once per relation-node** (N+1-safe). Depth-1 behaviour is unchanged when `spec.Deep` is null.

- [ ] **Step 1: Write the failing test**

Create `tests/Struo.Tests/Query/DeepNestingExpansionTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class DeepNestingExpansionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> Post(HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    [Fact]
    public async Task Depth2_m2o_chain_article_category_parent_resolves()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var grandparent = await Post(c, "category", new { name = "GP-8c3a" });
        var parent = await Post(c, "category", new { name = "P-8c3a", parentId = grandparent });
        var child = await Post(c, "category", new { name = "C-8c3a", parentId = parent });
        var articleId = await Post(c, "article",
            new { status = "draft", categoryId = child, translations = new { en = new { title = "A-8c3a" } } });

        // article -> category (child) -> parent (P) -> parent (GP): depth-3 M2O chain.
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new { id = new Dictionary<string, object> { ["_eq"] = articleId } },
            deep = new { category = new { deep = new { parent = new { deep = new { parent = new { } } } } } }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var row = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data")[0];
        var cat = row.GetProperty("category");
        cat.GetProperty("name").GetString().Should().Be("C-8c3a");
        var p = cat.GetProperty("parent");
        p.GetProperty("name").GetString().Should().Be("P-8c3a");
        p.GetProperty("parent").GetProperty("name").GetString().Should().Be("GP-8c3a"); // depth-3 resolved
    }

    [Fact]
    public async Task Depth2_o2m_then_m2o_category_articles_category_resolves()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var cat = await Post(c, "category", new { name = "O2MParent-8c3a" });
        await Post(c, "article",
            new { status = "draft", categoryId = cat, translations = new { en = new { title = "Kid-8c3a" } } });

        // category -> articles (O2M) -> category (M2O back to the same category): mixed-kind depth-2.
        var resp = await c.GetAsync(
            $"/api/items/category/{cat}?deep=" +
            Uri.EscapeDataString("{\"articles\":{\"deep\":{\"category\":{}}}}"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var articles = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("articles");
        articles.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        articles[0].GetProperty("category").GetProperty("name").GetString().Should().Be("O2MParent-8c3a");
    }

    [Fact]
    public async Task Depth2_self_ref_o2m_children_resolves_cjk()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var root = await Post(c, "category", new { name = "根-8c3a" });   // CJK: 根 = U+6839
        await Post(c, "category", new { name = "子-8c3a", parentId = root }); // 子 = U+5B50

        // category -> children (O2M self-ref) -> children (empty, but the level resolves).
        var resp = await c.GetAsync(
            $"/api/items/category/{root}?deep=" +
            Uri.EscapeDataString("{\"children\":{\"deep\":{\"children\":{}}}}"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var children = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("children");
        var childNames = children.EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToList();
        childNames.Should().Contain("子-8c3a"); // CJK round-trips; nested `children` key present on each
        children[0].TryGetProperty("children", out var grand).Should().BeTrue();
        grand.ValueKind.Should().Be(JsonValueKind.Array); // depth-2 self-ref level materialised
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DeepNestingExpansionTests"`
Expected: FAIL — nested keys (`category.parent.parent`, `articles[].category`, `children[].children`) are absent/null: the expander stops at depth 1.

- [ ] **Step 3: Write minimal implementation**

Replace `ExpandAsync` in `src/Struo.Infrastructure/Query/RelationExpander.cs` with the recursive version below. The three per-kind branches keep their existing query/group/order logic; each now records the `(target entity, projected mutable dict)` pairs it produces into a local `expanded` list, and a single post-switch block recurses into `spec.Deep` **once per relation** over the distinct target entities and merges the sub-results back by target id. `JunctionSortKey` / `SafeToInt` are unchanged (keep them as-is below `ExpandAsync`).

```csharp
    public async Task<Dictionary<object, Dictionary<string, object?>>> ExpandAsync(
        string collection, IReadOnlyList<object> parents, DeepSpec deep,
        Func<string, object, IReadOnlyList<string>?, IReadOnlyDictionary<string, object?>> projectTarget,
        Func<object, object> parentId, Func<object, string, object?> readProp,
        CancellationToken ct = default)
    {
        var result = new Dictionary<object, Dictionary<string, object?>>();
        foreach (var p in parents) result[parentId(p)] = new Dictionary<string, object?>();

        foreach (var (relName, spec) in deep.Relations)
        {
            var desc = graph.Descriptors(collection).FirstOrDefault(d =>
                           string.Equals(d.Meta.Name, relName, StringComparison.OrdinalIgnoreCase))
                       ?? throw new QueryException($"Unknown relation '{relName}' on '{collection}'.");
            var rel = desc.Meta;

            // (target entity, its projected mutable dict) pairs produced for this relation, so a
            // nested spec can recurse on the entities and merge sub-relations into the dicts.
            var expanded = new List<(object Entity, Dictionary<string, object?> Dict)>();

            switch (rel.Kind)
            {
                case RelationKind.ManyToOne:
                {
                    var fkValues = parents
                        .Select(p => readProp(p, rel.ForeignKey!))
                        .Where(v => v is not null)
                        .Distinct()
                        .ToList()!;
                    var targets = await repository.QueryWhereInAsync(rel.TargetCollection, "id", fkValues!, ct);
                    var byId = targets.ToDictionary(t => readProp(t, "id")!, t => t);
                    foreach (var p in parents)
                    {
                        var fk = readProp(p, rel.ForeignKey!);
                        if (fk is not null && byId.TryGetValue(fk, out var tr))
                        {
                            var d = (Dictionary<string, object?>)projectTarget(rel.TargetCollection, tr, spec.Fields);
                            result[parentId(p)][relName] = d;
                            expanded.Add((tr, d));
                        }
                        else result[parentId(p)][relName] = null;
                    }
                    break;
                }
                case RelationKind.OneToMany:
                {
                    var ids = parents.Select(parentId).ToList();
                    var children = await repository.QueryWhereInAsync(
                        rel.TargetCollection, desc.ReverseForeignKeyProperty!, ids, ct);
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
                case RelationKind.ManyToMany:
                {
                    var ids = parents.Select(parentId).ToList();
                    var junctions = await repository.QueryEntityWhereInAsync(
                        desc.JunctionType!, desc.JunctionParentFk!, ids, ct);
                    var targetIds = junctions
                        .Select(j => readProp(j, desc.JunctionTargetFk!)!)
                        .Distinct()
                        .ToList();
                    var targets = (await repository.QueryWhereInAsync(rel.TargetCollection, "id", targetIds, ct))
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
                default:
                    throw new QueryException($"Unsupported relation kind '{rel.Kind}' for '{relName}'.");
            }

            // Recurse ONCE per relation-node: expand the target's own relations over the DISTINCT
            // target entities (batched, breadth-first per level — N+1-safe), then merge each nested
            // relation value into the corresponding target dict by target id.
            if (spec.Deep is not null && expanded.Count > 0)
            {
                var distinct = expanded.Select(e => e.Entity).Distinct().ToList();
                var sub = await ExpandAsync(
                    rel.TargetCollection, distinct, spec.Deep, projectTarget, parentId, readProp, ct);
                foreach (var (entity, dict) in expanded)
                    if (sub.TryGetValue(parentId(entity), out var subMap))
                        foreach (var (k, v) in subMap) dict[k] = v;
            }
        }

        return result;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DeepNestingExpansionTests"`
Expected: PASS (3 tests). Then confirm depth-1 regression:
Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DeepExpansionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Query/RelationExpander.cs tests/Struo.Tests/Query/DeepNestingExpansionTests.cs
git commit -m "feat(query): recursive batched deep expansion (depth>1) (8c.3a)"
```

---

## Task 5: N+1 batching invariant test

**Files:**
- Create: `tests/Struo.Tests/Support/CountingItemRepository.cs`
- Test: `tests/Struo.Tests/Query/DeepNestingBatchingTests.cs` (create)

**Interfaces:**
- Consumes: `IItemRepository` (decorated), `RelationshipGraph` (resolved from `ApiFactory.Services`), `RelationExpander` (constructed directly).
- Produces: a locked invariant — the number of follow-up queries for a depth-2 expansion is **constant in the number of intermediate rows** (proves breadth-first batching, not per-parent N+1).

**Why direct, not via HTTP:** counting queries through the full HTTP stack is noisy (auth/permission/translation queries). Constructing `RelationExpander` directly with a counting `IItemRepository` and minimal delegates isolates exactly the expansion's batch queries; the id property on every entity is `Id`, so a trivial `parentId`/`readProp` suffices, and `projectTarget` need only return a mutable dict carrying the id.

- [ ] **Step 1: Write the failing test**

Create `tests/Struo.Tests/Support/CountingItemRepository.cs`:

```csharp
using Struo.Application.Query;

namespace Struo.Tests.Support;

/// <summary>
/// Forwards to a real <see cref="IItemRepository"/> and counts the batched follow-up queries
/// the deep expander issues (<see cref="QueryWhereInAsync"/> + <see cref="QueryEntityWhereInAsync"/>).
/// Used to assert the N+1 batching invariant.
/// </summary>
public sealed class CountingItemRepository(IItemRepository inner) : IItemRepository
{
    private int _whereInCalls;
    public int WhereInCalls => _whereInCalls;
    public void ResetCount() => _whereInCalls = 0;

    public Task<IReadOnlyList<object>> QueryWhereInAsync(
        string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default)
    {
        System.Threading.Interlocked.Increment(ref _whereInCalls);
        return inner.QueryWhereInAsync(collection, property, values, ct);
    }

    public Task<IReadOnlyList<object>> QueryEntityWhereInAsync(
        Type entityType, string propertyName, IReadOnlyList<object> values, CancellationToken ct = default)
    {
        System.Threading.Interlocked.Increment(ref _whereInCalls);
        return inner.QueryEntityWhereInAsync(entityType, propertyName, values, ct);
    }

    // Delegate every other member straight through (no counting).
    // NOTE: implement the remaining IItemRepository members by forwarding to `inner`.
    // The executor must add a forwarding override for each member declared on IItemRepository
    // (see src/Struo.Application/Query/IItemRepository.cs) — do not omit any, or the class
    // will not compile. Each is a one-line `=> inner.Member(args);`.
}
```

> **Executor note:** open `src/Struo.Application/Query/IItemRepository.cs` and add a one-line forwarding override for **every** member not already implemented above. Keep the two `WhereIn` overrides that increment the counter.

Create `tests/Struo.Tests/Query/DeepNestingBatchingTests.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class DeepNestingBatchingTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> Post(HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    private static readonly System.Reflection.BindingFlags Flags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.IgnoreCase;

    private static object? ReadProp(object e, string name) => e.GetType().GetProperty(name, Flags)?.GetValue(e);

    // Expands `category -> articles (O2M) -> category (M2O)` and returns how many WhereIn queries
    // the expander issued for a category that has `articleCount` articles.
    private async Task<int> ExpandCountForCategoryWith(int articleCount, string label)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var catId = await Post(c, "category", new { name = $"Batch-{label}" });
        for (var i = 0; i < articleCount; i++)
            await Post(c, "article", new
            {
                status = "draft", categoryId = catId,
                translations = new { en = new { title = $"Batch-{label}-{i}" } }
            });

        using var scope = _factory.Services.CreateScope();
        var real = scope.ServiceProvider.GetRequiredService<IItemRepository>();
        var graph = scope.ServiceProvider.GetRequiredService<RelationshipGraph>();
        var counter = new CountingItemRepository(real);
        var expander = new RelationExpander(counter, graph);

        var parents = await real.QueryWhereInAsync("category", "id", new object[] { System.Guid.Parse(catId) });

        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>
        {
            ["articles"] = new DeepRelationSpec(null, null,
                new DeepSpec(new Dictionary<string, DeepRelationSpec>
                {
                    ["category"] = new DeepRelationSpec(null, null)
                }))
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
    public async Task Depth2_expansion_query_count_is_constant_in_row_count()
    {
        var few = await ExpandCountForCategoryWith(2, "few");
        var many = await ExpandCountForCategoryWith(8, "many");

        // Batched (breadth-first per level): 1 query for `articles` + 1 for the nested `category`
        // = 2, regardless of how many articles exist. A per-parent (N+1) recursion would make the
        // nested-category query count grow with the article count, so `many > few`.
        few.Should().Be(many);
        many.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

First it must **compile** — the executor completes `CountingItemRepository`'s forwarding members. Then:
Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~DeepNestingBatchingTests"`
Expected: **PASS** if Task 4's recursion is already batched (this test guards that it stays batched). To confirm the test actually discriminates, temporarily change Task 4's recursion to call `ExpandAsync` **per parent** inside the merge loop and re-run — it must FAIL (`many` becomes 3 for 2 articles / 9 for 8). Revert the temporary change.

- [ ] **Step 3: (no new implementation)**

Task 4 already implements batched recursion. This task only adds the guard. If Step 2 shows `many == 2 == few`, proceed.

- [ ] **Step 4: Run the full Query suite**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~Struo.Tests.Query"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/Struo.Tests/Support/CountingItemRepository.cs tests/Struo.Tests/Query/DeepNestingBatchingTests.cs
git commit -m "test(query): lock N+1 batching invariant for deep expansion (8c.3a)"
```

---

## Task 6: Api — GraphQL recursive selection → nested `DeepSpec`

**Files:**
- Modify: `src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs` (`BuildQuery` signature)
- Modify: `src/Struo.Api/GraphQl/CollectionResolvers.cs` (`SelectionDeepSpec` + resolver call sites)
- Modify: `tests/Struo.Tests/GraphQl/GraphQlQueryBuilderTests.cs` (update to new signature)
- Test: `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs` (add depth-2 round-trip + over-depth negative; follow the file's existing execution harness)

**Interfaces:**
- Consumes: `DeepSpec` tree (Task 1); `SelectionRelations`' element-type resolution pattern (`CollectionResolvers.cs:82-110`); `IMetadataProvider.GetCollection(...).Relations` with `.Name` + `.TargetCollection`.
- Produces: `GraphQlQueryBuilder.BuildQuery(filter, sort, limit, offset, search, DeepSpec? deep, collection, relationTarget)`; `CollectionResolvers.SelectionDeepSpec(IResolverContext, string collection, IMetadataProvider, bool elementIsDirect)`.

- [ ] **Step 1: Write the failing test (builder signature)**

Update `tests/Struo.Tests/GraphQl/GraphQlQueryBuilderTests.cs`. Replace the four `BuildQuery(...)` call sites' relation argument: pass `deep:` (a `DeepSpec?`) instead of `requestedRelations:` (a `string[]`). Replace `BuildQuery_sets_Deep_only_when_relations_requested` with a pass-through test:

```csharp
    [Fact]
    public void BuildQuery_maps_limit_offset_search_and_filter()
    {
        var q = GraphQlQueryBuilder.BuildQuery(
            filter: new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "x" } },
            sort: new[] { "-id" }, limit: 5, offset: 10, search: "term",
            deep: null);

        q.Limit.Should().Be(5);
        q.Offset.Should().Be(10);
        q.Search.Should().Be("term");
        q.Filter.Should().NotBeNull();
        q.Deep.Should().BeNull();
    }

    [Fact]
    public void BuildQuery_passes_deep_through_unchanged()
    {
        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>
        {
            ["category"] = new DeepRelationSpec(null, null,
                new DeepSpec(new Dictionary<string, DeepRelationSpec> { ["parent"] = new DeepRelationSpec(null, null) }))
        });

        var q = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, deep);

        q.Deep.Should().BeSameAs(deep);
        q.Deep!.Relations["category"].Deep!.Relations.Should().ContainKey("parent");
    }

    [Fact]
    public void BuildQuery_defaults_limit_and_offset_to_zero_when_absent()
    {
        var q = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, deep: null);
        q.Limit.Should().Be(0);
        q.Offset.Should().Be(0);
    }
```

And in `BuildQuery_flattens_nested_M2O_relation_filter_to_dotted_path`, change `requestedRelations: System.Array.Empty<string>()` to `deep: null`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~GraphQlQueryBuilderTests"`
Expected: FAIL to **compile** — `BuildQuery` still expects `IReadOnlyList<string> requestedRelations`.

- [ ] **Step 3: Write minimal implementation (builder)**

Replace `BuildQuery` in `src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs` (keep `ParseSort` unchanged):

```csharp
    public static QueryModel BuildQuery(
        IReadOnlyDictionary<string, object?>? filter,
        IReadOnlyList<string>? sort,
        int? limit,
        int? offset,
        string? search,
        DeepSpec? deep,
        string collection = "",
        Func<string, string, string?>? relationTarget = null)
    {
        return new QueryModel(
            Fields: null,
            Filter: FilterInputTranslator.Translate(filter, collection, relationTarget),
            Sort: ParseSort(sort),
            Limit: limit ?? 0,
            Offset: offset ?? 0,
            Search: string.IsNullOrWhiteSpace(search) ? null : search)
        {
            Deep = deep
        };
    }
```

- [ ] **Step 4: Run builder test to verify it passes**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~GraphQlQueryBuilderTests"`
Expected: FAIL to compile in **`CollectionResolvers.cs`** now (its two `BuildQuery(...)` call sites still pass a flat list). Fix them in Step 5 — the builder unit tests themselves pass once the project compiles.

- [ ] **Step 5: Write minimal implementation (resolver recursion)**

In `src/Struo.Api/GraphQl/CollectionResolvers.cs`:

(a) Replace the two call sites to build a nested `DeepSpec` from the selection tree:

```csharp
    private static async ValueTask<object?> ResolveSingle(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var locale = ctx.ArgumentValue<string?>("locale");
        var metadata = ctx.Service<IMetadataProvider>();
        var deep = SelectionDeepSpec(ctx, collection, metadata, elementIsDirect: true);
        var data = await ctx.Service<IGraphQlDataSource>().GetAsync(collection, id, deep, locale, ctx.RequestAborted);
        return data;
    }

    private static async ValueTask<object?> ResolveList(IResolverContext ctx, string collection, IMetadataProvider metadata)
    {
        var filter = ctx.ArgumentValue<IReadOnlyDictionary<string, object?>?>("filter");
        var sort = ctx.ArgumentValue<IReadOnlyList<string>?>("sort");
        var limit = ctx.ArgumentValue<int?>("limit");
        var offset = ctx.ArgumentValue<int?>("offset");
        var search = ctx.ArgumentValue<string?>("search");
        var locale = ctx.ArgumentValue<string?>("locale");
        var deep = SelectionDeepSpec(ctx, collection, metadata, elementIsDirect: false);
        var query = GraphQlQueryBuilder.BuildQuery(
            filter, sort, limit, offset, search, deep,
            collection, RelationTargets(metadata));
        var page = await ctx.Service<IGraphQlDataSource>().QueryAsync(collection, query, locale, ctx.RequestAborted);
        return new PagedResultView(page.Data.Cast<object>().ToList(), page.Total);
    }
```

(b) Replace `SelectionRelations` with the recursive `SelectionDeepSpec` + a private `BuildDeep` helper (the direct-vs-`XList` element resolution is preserved from the old method; the aliased-duplicate dedup is now "first wins per level" via `ContainsKey`):

```csharp
    /// <summary>
    /// Builds a nested <see cref="DeepSpec"/> from the client's selection tree: each selected field
    /// that is a relation of the collection contributes a <see cref="DeepRelationSpec"/> whose nested
    /// <c>Deep</c> is built recursively from that relation's own sub-selection. Depth is bounded by
    /// ItemService's MaxRelationDepth validation and HotChocolate's max-execution-depth.
    /// </summary>
    internal static DeepSpec? SelectionDeepSpec(
        IResolverContext ctx, string collection, IMetadataProvider metadata, bool elementIsDirect)
    {
        ObjectType elementType;
        SelectionEnumerator childSelections;
        if (elementIsDirect)
        {
            elementType = (ObjectType)ctx.Selection.Field.Type.NamedType();
            childSelections = ctx.GetSelections(elementType);
        }
        else
        {
            var listType = (ObjectType)ctx.Selection.Field.Type.NamedType();          // XList
            var itemsSel = ctx.GetSelections(listType).FirstOrDefault(s => s.Field.Name == "items");
            if (itemsSel is null) return null;
            elementType = (ObjectType)itemsSel.Field.Type.NamedType();                  // X
            childSelections = ctx.GetSelections(elementType, itemsSel);
        }
        return BuildDeep(ctx, collection, childSelections, metadata);
    }

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
            map[rel.Name] = new DeepRelationSpec(null, null, nested);
        }
        return map.Count == 0 ? null : new DeepSpec(map);
    }
```

Ensure the file still has the `using`s it needs (`HotChocolate.Resolvers`, `Struo.Domain.Query`, `Struo.Application.Metadata`/wherever `IMetadataProvider` lives — the old `SelectionRelations` already used these types, so the set is unchanged except for `Struo.Domain.Query` if not already present).

- [ ] **Step 6: Run to verify the project compiles and builder tests pass**

Run: `dotnet build -warnaserror`
Expected: 0 warnings, 0 errors.
Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~GraphQlQueryBuilderTests"`
Expected: PASS.

- [ ] **Step 7: Write the failing GraphQL execution test (depth-2 round-trip + over-depth)**

Add to `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs`, mirroring the file's existing request-execution harness (how it POSTs a GraphQL query and reads `data`/`errors` — reuse the same helper the existing tests use). Two tests:

```csharp
    [Fact]
    public async Task Nested_relation_selection_resolves_depth_two()
    {
        // Seed grandparent <- parent <- child category, then an article in `child`.
        // (Reuse the harness's seeding/mutation helper if present; else create via REST like the
        //  GraphQl mutation tests do.)
        // Query: xs article filtered by id, selecting category { parent { name } }.
        const string query = @"query($id: ID!) {
          articles(filter: { id: { eq: $id } }) {
            items { id category { name parent { name } } }
          }
        }";
        // ...execute with { id = <articleId> } via the file's existing executor...
        // Assert: items[0].category.name == "<child>" AND
        //         items[0].category.parent.name == "<parent>"  (depth-2 resolves; was null pre-8c.3a)
    }

    [Fact]
    public async Task Over_depth_nested_selection_is_rejected()
    {
        // A category.parent chain nested 6 levels deep (> MaxRelationDepth = 5) must produce a
        // BAD_USER_INPUT error (ItemService depth validation) — or, if HotChocolate's max-execution
        // -depth (12) fires first for a deeper selection, a validation error. Assert `errors` is
        // non-empty and no partial `data` violates the cap.
        const string query = @"query($id: ID!) {
          categories(filter: { id: { eq: $id } }) {
            items { parent { parent { parent { parent { parent { parent { id } } } } } } }
          }
        }";
        // ...execute; assert result has errors (BAD_USER_INPUT from the depth cap)...
    }
```

> **Executor note:** implement these two tests concretely using `GraphQlExecutionTests.cs`'s existing execution/seeding helpers (do not invent a new harness). The **assertions** above are the contract: depth-2 `category.parent.name` resolves; the 6-deep `parent` chain yields a `BAD_USER_INPUT` (or HC depth) error.

- [ ] **Step 8: Run the execution tests**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~GraphQlExecutionTests"`
Expected: the two new tests PASS; all existing GraphQL execution tests stay green (depth-1 selections unchanged).

- [ ] **Step 9: Commit**

```bash
git add src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs src/Struo.Api/GraphQl/CollectionResolvers.cs tests/Struo.Tests/GraphQl/GraphQlQueryBuilderTests.cs tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs
git commit -m "feat(graphql): recursive selection builds nested DeepSpec (depth>1) (8c.3a)"
```

---

## Task 7: Docs — guide + ROADMAP

**Files:**
- Modify: `docs/guide/*` (the querying/relations guide page — locate the existing `deep` documentation)
- Modify: `docs/ROADMAP.md` (8c.3a status row + phase table)

**Interfaces:** none (documentation).

- [ ] **Step 1: Update the guide**

Find the guide page documenting `deep` expansion (grep `docs/guide` for `deep`). Add a "Multi-level (nested) expansion" subsection with:
- GraphQL example: `article(id:…) { category { parent { name } } }`.
- REST nested envelope example: `POST /api/items/article/query` with `{"deep":{"category":{"deep":{"parent":{}}}}}`.
- The `MaxRelationDepth` cap (default 5) and that over-depth → 400 / `BAD_USER_INPUT`.
- Note the depth-vs-count semantics change (sibling relation count is no longer capped; nesting depth is).
- Cross-reference: to-many nested-list `filter/sort/limit/offset` arguments are **8c.3b** (not yet available); nested relation lists currently return all rows.

- [ ] **Step 2: Update ROADMAP**

In `docs/ROADMAP.md`:
- Add an 8c.3a status bullet (done & automated-gates-green; live-gate pending) mirroring the prior 8c bullets' shape, and a verification baseline line (new backend test count).
- Split the existing `| 8c.3 | … | ⬜ planned |` phase-table row into `8c.3a` (this slice — mark done pending live gate) and `8c.3b` (nested-list args — ⬜ planned), pointing 8c.3a at this spec + plan.
- Update the "Next up" section: 8c.3a done; remaining = 8c.3b (nested-list args) or Phase 9.

- [ ] **Step 3: Full build + test**

Run: `dotnet build -warnaserror` → 0 warnings.
Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj`
Expected: all green (baseline 534 + new tests from Tasks 1–6). Record the new count for the ROADMAP baseline line.

- [ ] **Step 4: Commit**

```bash
git add docs/ROADMAP.md docs/guide
git commit -m "docs(roadmap,guide): Phase 8c.3a multi-level relation nesting (automated gates green; live gate pending)"
```

---

## Live Gate (user-run, after Task 7)

Not an automated task — the SQLite-green ≠ Postgres-correct discipline requires the real Postgres DB (`web-struo-cms-db`). Hand the executor's owner this checklist (spec §12):

1. Seed a category chain (root → child → grandchild) + an article in the deepest child; an M2M tag on the article.
2. GraphQL depth-3 M2O chain (`article { category { parent { parent { name } } } }`) resolves the full chain.
3. GraphQL depth-2 O2M (`categories { items { children { name } } }`) resolves.
4. Mixed-kind multi-hop (`categories { items { articles { category { name } } } }`) resolves.
5. **CJK** name at a nested level round-trips code-point-exact.
6. Self-referential cycle (`category.parent.parent`) resolves without loop.
7. Over-depth nested selection → `BAD_USER_INPUT`.
8. REST nested envelope (`deep={"category":{"deep":{"parent":{}}}}`) resolves on real PG.

Record queries + responses as evidence. Any SQLite-green ≠ Postgres-correct fix lands in Infrastructure and is committed as a live-gate fix (like prior phases).

---

## Self-Review (completed by the plan author)

**Spec coverage:** §5 recursive spec → Task 1. §6 engine recursion → Task 4. §6.1 N+1 invariant → Task 5. §7 depth/name validation → Task 3. §8 GraphQL selection recursion + `BuildQuery` → Task 6. §9 REST envelope recursion → Task 2. §10 error handling → Tasks 3 (validation → `BAD_USER_INPUT`) + 6 (over-depth). §11 security (projection at every level) → inherited by Task 4's `projectTarget` reuse (no code needed). §12 testing → Tasks 1–6 automated + live-gate checklist. §13 acceptance → Task 7 build/test + live gate. §14 files → all mapped. No spec section is left without a task.

**Placeholder scan:** the only deferred-detail points are the two GraphQL execution tests (Task 6 Step 7) and the guide/ROADMAP edits (Task 7), each with an explicit contract (assertions / required content) and a pointer to the existing harness/page — not "TBD". `CountingItemRepository` (Task 5) has an explicit executor note to forward the remaining `IItemRepository` members (the interface is small; enumerated at the source path given).

**Type consistency:** `DeepRelationSpec(Fields, Limit, Deep)` and `DeepSpec(Relations)` are used identically across Tasks 1–6. `BuildQuery(..., DeepSpec? deep, ...)` matches its call sites (Task 6a) and unit tests (Task 6 Step 1). `SelectionDeepSpec(IResolverContext, string, IMetadataProvider, bool)` matches both call sites. `graph.Resolve(...) → RelationMetadata?.TargetCollection` (Task 3) matches the confirmed source signature. `RelationExpander(counter, graph)` matches its `(IItemRepository, RelationshipGraph)` constructor (Task 5).
