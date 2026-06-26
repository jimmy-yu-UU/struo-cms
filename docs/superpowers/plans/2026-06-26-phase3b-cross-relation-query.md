# Phase 3b — Cross-Relation Filter/Sort Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the query DSL filter and sort across relations via dotted field paths (`category.name`, `category.parent.name`, `tags.name`), replacing the Phase-2 400 rejection with graph-validated execution.

**Architecture:** Approach A (hybrid). **Filters** resolve via two-phase id-resolution: walk the relation path leaf→root through the relationship graph, one batched `IN` query per hop, ending in an own-collection `id IN (…)` condition that drops into the existing Phase-2 `ConditionalModel` pipeline (empty set → `id IS NULL`, an always-false PK leaf). The rewrite runs in `ItemService` via a new `IRelationFilterResolver` to avoid a DI cycle. **Sort** (to-one paths only) executes in the repository's `BuildOrderBy` via the spike-chosen mechanism. Zero vendor SQL; all hops are SqlSugar `Queryable`/`ConditionalModel`.

**Tech Stack:** .NET 10, C# latest, SqlSugarCore, ASP.NET Core controllers, xUnit + AwesomeAssertions, SQLite (tests) / PostgreSQL (dev).

## Global Constraints

- All DB access via SqlSugar ORM; **zero vendor SQL** (§17.4).
- Query DSL never leaks ORM internals; field/relation paths are whitelist-validated against the relationship graph (§17.7).
- Dependency rule (§2): Domain → nothing; Application → Domain; Infrastructure → Application+Domain; Api → Application+Infrastructure. Domain stays dependency-free; persistence attributes live on sample entities only.
- Outbound JSON = camelCase.
- Packages via `dotnet add package` (latest, CPM in `Directory.Packages.props`); never hardcode versions.
- Path depth bounded by `StruoQueryOptions.MaxRelationDepth` (existing, default 5). **No** `MaxRelationFilterIds` cap (deliberately omitted per spec review).
- Capabilities: to-one filter, to-one sort, to-many EXISTS filter. **To-many sort is rejected (400).** Sort path containing any to-many segment → 400.
- Empty relation-filter result → `id IS NULL` leaf (portable always-false), never `IN ()`; returns 0 rows, not an error.
- Tests follow existing conventions: unit tests in `tests/Struo.Tests/Query/`; integration tests use `[Collection("ApiIntegration")]` with `ApiFactory` and the Blog sample, asserting over HTTP (see `DeepExpansionTests.cs`).
- Run the full suite with: `dotnet test`.

---

### Task 1: Spike — choose the sort mechanism (throwaway, NOT merged)

**Goal:** Settle how to-one (and multi-level to-one) sort is expressed in SqlSugar against SQLite **and** PostgreSQL, before writing Task 5. This task produces a recorded decision, not merged code.

**Files:**
- Create (scratch, deleted at end): `spike/SortSpike/` console or a `[Fact(Skip=...)]` scratch test — your choice; do not add it to `Struo.Tests` permanently.

**Interfaces:**
- Consumes: `RelationshipGraph` descriptors, `ISqlSugarClient`, the Blog sample entities (`Article`, `Category`).
- Produces: a findings note (pasted into your report) fixing the sort mechanism for Task 5.

- [ ] **Step 1: Exercise the leading candidate** — to-one sort via `Queryable<Article>().OrderBy(string)` hosting a correlated subquery built from `db.EntityMaintenance.GetTableName(...)` / `GetDbColumnName(...)`:
  `(SELECT c.{nameCol} FROM {categoryTable} c WHERE c.{idCol} = {articleTable}.{categoryIdCol}) DESC`.
  Confirm SqlSugar emits it and that the **main-table correlation reference resolves** (the crux — verify the alias/table-name SqlSugar uses for the root table). Run against SQLite; then point the same code at the dev PostgreSQL connection and confirm it renders.

- [ ] **Step 2: Exercise the fallback** — a narrow `LeftJoin<Category>((a,c) => a.CategoryId == c.Id).OrderBy((a,c) => c.Name)` for the sort path only. Note whether a to-one left join keeps row counts stable (it must, for paging/`Total`).

- [ ] **Step 3: Exercise multi-level** — `category.parent.name` via nested correlated subquery (or chained join). Confirm feasibility.

- [ ] **Step 4: Sanity-check `Subqueryable`** — `Queryable<Article>().Where(a => SqlFunc.Subqueryable<ArticleTag>().Where(j => j.ArticleId == a.Id).Any())` to note whether it is clean enough to optionally drive filters later (informational only; filters use two-phase regardless).

- [ ] **Step 5: Record the decision and delete the spike.** In your report, state: chosen sort mechanism (correlated-subquery-string **or** LeftJoin), whether multi-level sort is supported or reduced to single-hop, and any aliasing caveat. Delete the spike code (`git status` clean of spike files). **No commit** for this task (nothing merges); the report carries the outcome.

**Controller note:** Before dispatching Task 5, fold the chosen mechanism into Task 5's Step 3 code (the task ships both the correlated-subquery primary and the LeftJoin appendix — keep the one the spike chose).

---

### Task 2: `RelationPath` parse/validate + wire into `QueryValidator`

**Goal:** Parse a dotted path into relation segments + leaf, validate it against the graph + metadata + depth cap, and classify sortability. Replace the Phase-2 dotted-path rejection.

**Files:**
- Create: `src/Struo.Application/Query/RelationPath.cs`
- Modify: `src/Struo.Application/Query/QueryValidator.cs`
- Modify: `src/Struo.Application/Query/ItemService.cs:30` (pass graph + metadata to `Validate`)
- Test: `tests/Struo.Tests/Query/RelationPathTests.cs` (new), `tests/Struo.Tests/Query/QueryValidatorTests.cs` (update)

**Interfaces:**
- Consumes: `IRelationshipGraph` (`Resolve(collection, relationName)`), `IMetadataProvider` (`GetCollection(name)`), `StruoQueryOptions.MaxRelationDepth`, `RelationMetadata` (`Kind`, `TargetCollection`, `ForeignKey`), `RelationKind`.
- Produces:
  - `RelationPath` with `IReadOnlyList<RelationSegment> Segments`, `string LeafField`, `string TerminalCollection`, `bool IsSortable`.
  - `RelationSegment(string RelationName, RelationMetadata Relation, string DeclaringCollection)`.
  - `static bool RelationPath.IsRelationPath(string path)`; `static RelationPath RelationPath.Parse(string rootCollection, string path, IRelationshipGraph graph, IMetadataProvider metadata, int maxDepth)`.
  - `QueryValidator.Validate(QueryModel q, CollectionMetadata meta, StruoQueryOptions opts, IRelationshipGraph graph, IMetadataProvider metadata)`.

- [ ] **Step 1: Write the failing unit test** — `tests/Struo.Tests/Query/RelationPathTests.cs`:

```csharp
using AwesomeAssertions;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class RelationPathTests
{
    // Minimal fakes so the unit test does not need a DB or the real scan.
    private sealed class FakeGraph : IRelationshipGraph
    {
        public IReadOnlyList<RelationMetadata> Relations(string c) => [];
        public IReadOnlyList<(string, string)> InboundRestrict(string c) => [];
        public RelationMetadata? Resolve(string collection, string rel) => (collection, rel) switch
        {
            ("article", "category") => new RelationMetadata { Name = "category", Label = "Category",
                Kind = RelationKind.ManyToOne, TargetCollection = "category", Interface = RelationInterface.Dropdown, ForeignKey = "categoryId" },
            ("category", "parent") => new RelationMetadata { Name = "parent", Label = "Parent",
                Kind = RelationKind.ManyToOne, TargetCollection = "category", Interface = RelationInterface.TreeSelect, ForeignKey = "parentId", SelfReferencing = true },
            ("article", "tags") => new RelationMetadata { Name = "tags", Label = "Tags",
                Kind = RelationKind.ManyToMany, TargetCollection = "tag", Interface = RelationInterface.TagSelect },
            _ => null
        };
    }

    private sealed class FakeMeta : IMetadataProvider
    {
        public IReadOnlyList<CollectionMetadata> GetCollections() => [];
        public CollectionMetadata? GetCollection(string name) => name switch
        {
            "category" => new CollectionMetadata { Name = "category", Label = "Category", FieldGroups = [],
                Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }] },
            "tag" => new CollectionMetadata { Name = "tag", Label = "Tag", FieldGroups = [],
                Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }] },
            _ => null
        };
    }

    private static readonly IRelationshipGraph Graph = new FakeGraph();
    private static readonly IMetadataProvider Md = new FakeMeta();

    [Fact]
    public void Parses_single_hop_to_one()
    {
        var p = RelationPath.Parse("article", "category.name", Graph, Md, 5);
        p.Segments.Should().HaveCount(1);
        p.TerminalCollection.Should().Be("category");
        p.LeafField.Should().Be("name");
        p.IsSortable.Should().BeTrue();
    }

    [Fact]
    public void Parses_multi_level_to_one_and_is_sortable()
    {
        var p = RelationPath.Parse("article", "category.parent.name", Graph, Md, 5);
        p.Segments.Should().HaveCount(2);
        p.IsSortable.Should().BeTrue();
    }

    [Fact]
    public void Many_to_many_path_is_not_sortable()
    {
        var p = RelationPath.Parse("article", "tags.name", Graph, Md, 5);
        p.IsSortable.Should().BeFalse();
    }

    [Fact]
    public void Unknown_segment_throws()
    {
        var act = () => RelationPath.Parse("article", "ghost.name", Graph, Md, 5);
        act.Should().Throw<QueryException>().WithMessage("*ghost*");
    }

    [Fact]
    public void Unknown_leaf_field_throws()
    {
        var act = () => RelationPath.Parse("article", "category.nope", Graph, Md, 5);
        act.Should().Throw<QueryException>().WithMessage("*nope*");
    }

    [Fact]
    public void Over_depth_throws()
    {
        var act = () => RelationPath.Parse("article", "category.parent.name", Graph, Md, 1);
        act.Should().Throw<QueryException>().WithMessage("*depth*");
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test --filter "FullyQualifiedName~RelationPathTests"`. Expected: FAIL (compile error — `RelationPath` does not exist).

- [ ] **Step 3: Implement `RelationPath`** — `src/Struo.Application/Query/RelationPath.cs`:

```csharp
// src/Struo.Application/Query/RelationPath.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>One relation hop in a dotted path, plus the collection it is declared on.</summary>
public sealed record RelationSegment(string RelationName, RelationMetadata Relation, string DeclaringCollection);

/// <summary>
/// A validated dotted query path: a sequence of relation segments ending in a scalar leaf
/// field on the terminal collection. Built and validated against the relationship graph and
/// collection metadata; throws <see cref="QueryException"/> on any invalid segment, unknown
/// leaf, or over-depth path.
/// </summary>
public sealed class RelationPath
{
    public IReadOnlyList<RelationSegment> Segments { get; }
    public string LeafField { get; }
    public string TerminalCollection { get; }

    /// <summary>True iff every segment is to-one (M2O) — the only paths that can be sorted on.</summary>
    public bool IsSortable => Segments.All(s => s.Relation.Kind == RelationKind.ManyToOne);

    private RelationPath(IReadOnlyList<RelationSegment> segments, string leafField, string terminalCollection)
    {
        Segments = segments;
        LeafField = leafField;
        TerminalCollection = terminalCollection;
    }

    public static bool IsRelationPath(string path) => path.Contains('.');

    public static RelationPath Parse(
        string rootCollection, string path,
        IRelationshipGraph graph, IMetadataProvider metadata, int maxDepth)
    {
        var parts = path.Split('.');
        if (parts.Length < 2)
            throw new QueryException($"'{path}' is not a relation path.");

        var relCount = parts.Length - 1;          // last part is the leaf field
        if (relCount > maxDepth)
            throw new QueryException(
                $"Relation path '{path}' exceeds the maximum depth of {maxDepth}.");

        var segments = new List<RelationSegment>(relCount);
        var current = rootCollection;
        for (var i = 0; i < relCount; i++)
        {
            var relName = parts[i];
            var rel = graph.Resolve(current, relName)
                ?? throw new QueryException(
                    $"Unknown relation '{relName}' on '{current}' in path '{path}'.");
            segments.Add(new RelationSegment(relName, rel, current));
            current = rel.TargetCollection;
        }

        var leaf = parts[^1];
        var terminal = metadata.GetCollection(current)
            ?? throw new QueryException($"Unknown collection '{current}' in path '{path}'.");
        var leafKnown =
            string.Equals(leaf, "id", StringComparison.OrdinalIgnoreCase) ||
            terminal.Fields.Any(f => string.Equals(f.Name, leaf, StringComparison.OrdinalIgnoreCase));
        if (!leafKnown)
            throw new QueryException(
                $"Unknown field '{leaf}' on collection '{current}' in path '{path}'.");

        return new RelationPath(segments, leaf, current);
    }
}
```

- [ ] **Step 4: Run to verify it passes** — `dotnet test --filter "FullyQualifiedName~RelationPathTests"`. Expected: PASS (6/6). (If `IMetadataProvider` / `CollectionMetadata` / `FieldMetadata` / `RelationInterface` member names differ from the fakes above, align the test fakes to the real types — do not change production types.)

- [ ] **Step 5: Wire `QueryValidator` to use it.** Replace `src/Struo.Application/Query/QueryValidator.cs` body of `Validate` so it accepts the graph + metadata and delegates dotted paths. New signature and `CheckField`:

```csharp
public static QueryModel Validate(
    QueryModel q, CollectionMetadata meta, StruoQueryOptions opts,
    IRelationshipGraph graph, IMetadataProvider metadata)
{
    var known = meta.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    void CheckField(string path, bool forSort = false, bool allowRelation = true)
    {
        if (RelationPath.IsRelationPath(path))
        {
            if (!allowRelation)
                throw new QueryException($"Relation paths are not supported in field selection: '{path}'.");
            var rp = RelationPath.Parse(meta.Name, path, graph, metadata, opts.MaxRelationDepth);
            if (forSort && !rp.IsSortable)
                throw new QueryException($"Sort across to-many relations is not supported: '{path}'.");
            return;
        }
        if (string.Equals(path, "id", StringComparison.OrdinalIgnoreCase)) return;
        if (!known.Contains(path))
            throw new QueryException($"Unknown field '{path}' on collection '{meta.Name}'.");
    }
    // ... condition-count Walk unchanged, but its CheckField(c.FieldPath) call now resolves relations ...
    // sort:    foreach (var s in q.Sort) CheckField(s.Field, forSort: true);
    // fields:  if (q.Fields is not null) foreach (var f in q.Fields) CheckField(f, allowRelation: false);
    // limit/offset clamp unchanged.
}
```

Keep the existing `conditionCount` `Walk` (single-level `_and`/`_or` rule) exactly as-is; only `CheckField`'s body and the three call sites change. `SearchableFields` is unchanged.

- [ ] **Step 6: Update the caller** — `src/Struo.Application/Query/ItemService.cs:30`:

```csharp
var validated = QueryValidator.Validate(raw, meta, options, graph, metadata);
```

(`graph` and `metadata` are already injected into `ItemService`.)

- [ ] **Step 7: Update `QueryValidatorTests`.** Every `QueryValidator.Validate(...)` call now needs a graph + metadata. Add the two fakes from Step 1 (or a tiny local copy) and thread them through. **Replace** `Dotted_relation_path_throws` with a now-passing test:

```csharp
[Fact]
public void Dotted_relation_path_is_accepted_when_valid()
{
    var q = new QueryModel(null, new ComparisonFilter("category.name", QueryOperator.Eq, "x"), [], 0, 0, null);
    var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md);
    act.Should().NotThrow();
}

[Fact]
public void Sort_across_to_many_throws()
{
    var q = new QueryModel(null, null, [new SortField("tags.name", false)], 0, 0, null);
    var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md);
    act.Should().Throw<QueryException>().WithMessage("*to-many*");
}
```

where `Graph`/`Md` are the fakes (article→category[M2O], article→tags[M2M]; category/tag metadata expose `name`). Adjust the other existing tests to pass `Graph, Md`.

- [ ] **Step 8: Run the full suite** — `dotnet test`. Expected: PASS (existing count + new RelationPath/validator tests; previous `Dotted_relation_path_throws` replaced).

- [ ] **Step 9: Commit**

```bash
git add src/Struo.Application/Query/RelationPath.cs src/Struo.Application/Query/QueryValidator.cs src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/RelationPathTests.cs tests/Struo.Tests/Query/QueryValidatorTests.cs
git commit -m "feat: validate cross-relation dotted filter/sort paths against the graph"
```

---

### Task 3: Two-phase filter resolver — to-one (single + multi-level) + AST rewrite

**Goal:** Resolve a dotted to-one filter condition to an own-collection `id IN (…)` (empty → `id IS NULL`) and rewrite the filter AST in `ItemService` before the repository runs. Handles single-hop and multi-level all-to-one paths.

**Files:**
- Create: `src/Struo.Application/Query/IRelationFilterResolver.cs`
- Create: `src/Struo.Infrastructure/Query/RelationFilterResolver.cs`
- Modify: `src/Struo.Application/Query/IItemRepository.cs` (add `QueryIdsAsync`)
- Modify: `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` (implement `QueryIdsAsync`)
- Modify: `src/Struo.Application/Query/ItemService.cs` (inject resolver; rewrite filter in `QueryAsync`)
- Modify: the Infrastructure DI registration (register `IRelationFilterResolver`)
- Test: `tests/Struo.Tests/Query/CrossRelationFilterTests.cs` (new)

**Interfaces:**
- Consumes: `IItemRepository.QueryWhereInAsync`, `QueryEntityWhereInAsync`, the new `QueryIdsAsync`; `RelationshipGraph.Descriptors(collection)` (concrete, exposing `.Meta`, `.ReverseForeignKeyProperty`, `.JunctionType`, `.JunctionParentFk`, `.JunctionTargetFk`); `IEntityRegistry`; `RelationPath`.
- Produces:
  - `IRelationFilterResolver.RewriteAsync(string rootCollection, FilterNode? filter, CancellationToken ct) : Task<FilterNode?>`.
  - `IItemRepository.QueryIdsAsync(string collection, FilterNode leafCondition, CancellationToken ct) : Task<IReadOnlyList<object>>`.

- [ ] **Step 1: Write the failing integration test** — `tests/Struo.Tests/Query/CrossRelationFilterTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class CrossRelationFilterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static object Eq(object v) => new Dictionary<string, object> { ["_eq"] = v };

    private async Task<long> Post(System.Net.Http.HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

    [Fact]
    public async Task Filter_to_one_category_name()
    {
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A1" });
        var news = await Post(c, "category", new { name = "FilterNews" });
        var other = await Post(c, "category", new { name = "FilterOther" });
        var hit = await Post(c, "article", new { title = "HIT", status = "draft", authorId = author, categoryId = news });
        await Post(c, "article", new { title = "MISS", status = "draft", authorId = author, categoryId = other });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.name"] = Eq("FilterNews") }
        });
        var resp = await c.PostAsJsonAsync("/api/items/article/query", envelope);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        ids.Should().Contain(hit);
        foreach (var r in data.EnumerateArray())
            r.GetProperty("title").GetString().Should().Be("HIT");
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
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A2" });
        var parent = await Post(c, "category", new { name = "ParentCat" });
        var child = await Post(c, "category", new { name = "ChildCat", parentId = parent });
        var hit = await Post(c, "article", new { title = "DEEPHIT", status = "draft", authorId = author, categoryId = child });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["category.parent.name"] = Eq("ParentCat") }
        });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
        data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).Should().Contain(hit);
    }

    [Fact]
    public async Task Filter_relation_path_or_scalar_composes()
    {
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "A3" });
        var cat = await Post(c, "category", new { name = "OrCat" });
        var byCat = await Post(c, "article", new { title = "ZZZ", status = "draft", authorId = author, categoryId = cat });
        var byTitle = await Post(c, "article", new { title = "OrTitleUnique", status = "draft", authorId = author });

        // _or over a relation-path condition and a scalar condition
        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object>
            {
                ["_or"] = new object[]
                {
                    new Dictionary<string, object> { ["category.name"] = Eq("OrCat") },
                    new Dictionary<string, object> { ["title"] = Eq("OrTitleUnique") }
                }
            }
        });
        var ids = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync())
            .GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        ids.Should().Contain(byCat).And.Contain(byTitle);
    }
}
```

> Note on the `_or`/`filter` envelope shape: confirm the exact transport keys against `QueryParserTests.cs` (the parser is unchanged in 3b). If the parser expects a different envelope for logical groups, match it — do not change the parser.

- [ ] **Step 2: Run to verify it fails** — `dotnet test --filter "FullyQualifiedName~CrossRelationFilterTests"`. Expected: FAIL (currently dotted filters 400, or resolver missing).

- [ ] **Step 3: Add `QueryIdsAsync` to the repository port** — `src/Struo.Application/Query/IItemRepository.cs`:

```csharp
/// <summary>
/// Returns the primary-key values of all rows of <paramref name="collection"/> matching a
/// single own-collection (non-dotted) <paramref name="leafCondition"/>. Used by the
/// cross-relation filter resolver as the leaf step of two-phase id-resolution.
/// </summary>
Task<IReadOnlyList<object>> QueryIdsAsync(string collection, FilterNode leafCondition, CancellationToken ct = default);
```

- [ ] **Step 4: Implement `QueryIdsAsync`** in `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` (add a cached generic def beside the others, plus the method):

```csharp
private static readonly MethodInfo QueryIdsGenericAsyncDef =
    typeof(SqlSugarItemRepository).GetMethod(nameof(QueryIdsGenericAsync),
        BindingFlags.NonPublic | BindingFlags.Instance,
        [typeof(List<IConditionalModel>), typeof(string), typeof(CancellationToken)])!;

public async Task<IReadOnlyList<object>> QueryIdsAsync(
    string collection, FilterNode leafCondition, CancellationToken ct = default)
{
    var d = Descriptor(collection);
    var conditionals = ConditionalModelTranslator.Translate(leafCondition, null, [], d, db);
    var method = QueryIdsGenericAsyncDef.MakeGenericMethod(d.EntityType);
    return await (Task<IReadOnlyList<object>>)method.Invoke(this, [conditionals, d.IdProperty, ct])!;
}

private async Task<IReadOnlyList<object>> QueryIdsGenericAsync<T>(
    List<IConditionalModel> conditionals, string idProperty, CancellationToken ct) where T : class, new()
{
    var rows = await db.Queryable<T>().Where(conditionals).ToListAsync(ct);
    var pi = typeof(T).GetProperty(idProperty)!;
    return rows.Select(r => pi.GetValue(r)!).ToList();
}
```

- [ ] **Step 5: Define the resolver port** — `src/Struo.Application/Query/IRelationFilterResolver.cs`:

```csharp
// src/Struo.Application/Query/IRelationFilterResolver.cs
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Rewrites every dotted (cross-relation) <see cref="ComparisonFilter"/> in a filter tree into
/// an own-collection <c>id IN (…)</c> condition (or <c>id IS NULL</c> for an empty match) by
/// resolving the relation path to a set of root ids. Non-dotted nodes and logical structure
/// pass through unchanged, preserving single-level <c>_and</c>/<c>_or</c> composition.
/// </summary>
public interface IRelationFilterResolver
{
    Task<FilterNode?> RewriteAsync(string rootCollection, FilterNode? filter, CancellationToken ct = default);
}
```

- [ ] **Step 6: Implement the resolver** — `src/Struo.Infrastructure/Query/RelationFilterResolver.cs` (to-one hops in this task; O2M/M2M added in Task 4):

```csharp
// src/Struo.Infrastructure/Query/RelationFilterResolver.cs
using System.Reflection;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.Query;

public sealed class RelationFilterResolver(
    IItemRepository repository,
    RelationshipGraph graph,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    StruoQueryOptions options) : IRelationFilterResolver
{
    public async Task<FilterNode?> RewriteAsync(string rootCollection, FilterNode? filter, CancellationToken ct = default)
    {
        switch (filter)
        {
            case null:
                return null;
            case ComparisonFilter c when RelationPath.IsRelationPath(c.FieldPath):
                var ids = await ResolveRootIdsAsync(rootCollection, c, ct);
                return ids.Count == 0
                    ? new ComparisonFilter("id", QueryOperator.Null, null)        // always-false PK leaf
                    : new ComparisonFilter("id", QueryOperator.In, ids);
            case ComparisonFilter:
                return filter;                                                     // own-collection leaf
            case LogicalFilter l:
                var children = new List<FilterNode>(l.Children.Count);
                foreach (var ch in l.Children)
                    children.Add((await RewriteAsync(rootCollection, ch, ct))!);
                return new LogicalFilter(l.Op, children);
            default:
                return filter;
        }
    }

    private async Task<IReadOnlyList<object>> ResolveRootIdsAsync(
        string rootCollection, ComparisonFilter c, CancellationToken ct)
    {
        var path = RelationPath.Parse(rootCollection, c.FieldPath, graph, metadata, options.MaxRelationDepth);

        // Leaf: ids in the terminal collection matching "leaf <op> value".
        var leafCondition = new ComparisonFilter(path.LeafField, c.Op, c.Value);
        IReadOnlyList<object> set = await repository.QueryIdsAsync(path.TerminalCollection, leafCondition, ct);

        // Walk back leaf -> root, one hop per segment.
        for (var i = path.Segments.Count - 1; i >= 0; i--)
        {
            if (set.Count == 0) return set;
            set = await HopAsync(path.Segments[i], set, ct);
        }
        return set;
    }

    private async Task<IReadOnlyList<object>> HopAsync(
        RelationSegment seg, IReadOnlyList<object> targetIds, CancellationToken ct)
    {
        var desc = graph.Descriptors(seg.DeclaringCollection)
            .First(d => string.Equals(d.Meta.Name, seg.RelationName, StringComparison.OrdinalIgnoreCase));

        switch (seg.Relation.Kind)
        {
            case RelationKind.ManyToOne:
            {
                // declaring rows where FK IN targetIds -> their ids
                var declaringType = registry.Get(seg.DeclaringCollection)!.EntityType;
                var fkClr = registry.Get(seg.DeclaringCollection)!.FieldToProperty
                    .TryGetValue(seg.Relation.ForeignKey!, out var p) ? p : Capitalize(seg.Relation.ForeignKey!);
                var parents = await repository.QueryEntityWhereInAsync(declaringType, fkClr, targetIds, ct);
                return ReadIds(parents, "id");
            }
            // OneToMany and ManyToMany are added in Task 4.
            default:
                throw new QueryException(
                    $"Cross-relation filter for kind '{seg.Relation.Kind}' is not implemented yet.");
        }
    }

    private static IReadOnlyList<object> ReadIds(IReadOnlyList<object> rows, string property) =>
        rows.Select(r => ReadProp(r, property)).Where(v => v is not null).Distinct().ToList()!;

    private static object? ReadProp(object entity, string property) =>
        entity.GetType().GetProperty(property,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(entity);

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
```

> If `EntityDescriptor` exposes `FieldToProperty` under a different name, or `RelationshipGraph.Descriptors(...)` items expose the M2O FK CLR name directly, prefer that. The goal: query the declaring entity by its FK column for `IN targetIds`, mirroring how `RelationExpander` queries the junction by `JunctionParentFk`.

- [ ] **Step 7: Rewrite the filter in `ItemService.QueryAsync`** — inject `IRelationFilterResolver relationFilter` into the `ItemService` primary constructor, then in `QueryAsync` after validation and before the repository call:

```csharp
var validated = QueryValidator.Validate(raw, meta, options, graph, metadata);
validated = validated with { Filter = await relationFilter.RewriteAsync(collection, validated.Filter, ct) };
var searchable = QueryValidator.SearchableFields(meta);
var result = await repository.QueryAsync(collection, validated, searchable, ct);
```

- [ ] **Step 8: Register the resolver** in the Infrastructure DI extension (where `IItemRepository`/`IRelationExpander` are registered — find with `grep -rn "IRelationExpander" src/Struo.Infrastructure`):

```csharp
services.AddScoped<IRelationFilterResolver, RelationFilterResolver>();
```

- [ ] **Step 9: Run the new tests** — `dotnet test --filter "FullyQualifiedName~CrossRelationFilterTests"`. Expected: PASS for the to-one, empty-match, multi-level, and `_or` composition tests.

- [ ] **Step 10: Run the full suite** — `dotnet test`. Expected: all green (no Phase-2 regression).

- [ ] **Step 11: Commit**

```bash
git add src/Struo.Application/Query/IRelationFilterResolver.cs src/Struo.Infrastructure/Query/RelationFilterResolver.cs src/Struo.Application/Query/IItemRepository.cs src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/CrossRelationFilterTests.cs
# also add the DI registration file you edited in Step 8
git commit -m "feat: two-phase id-resolution for to-one cross-relation filters"
```

---

### Task 4: Extend filter resolver to to-many EXISTS (O2M + M2M)

**Goal:** Add the O2M and M2M hops to `RelationFilterResolver.HopAsync` so to-many relation filters work with EXISTS semantics (a root row matches if ≥1 child matches).

**Files:**
- Modify: `src/Struo.Infrastructure/Query/RelationFilterResolver.cs` (`HopAsync`)
- Test: `tests/Struo.Tests/Query/CrossRelationFilterTests.cs` (add cases)

**Interfaces:**
- Consumes: `RelationshipGraph.Descriptors(...)` items — `ReverseForeignKeyProperty` (O2M child FK CLR name), `JunctionType` / `JunctionParentFk` / `JunctionTargetFk` (M2M); `IItemRepository.QueryWhereInAsync` / `QueryEntityWhereInAsync`.
- Produces: full to-one + to-many resolution.

- [ ] **Step 1: Add failing tests** to `CrossRelationFilterTests.cs`:

```csharp
[Fact]
public async Task Filter_m2m_tags_name_exists()
{
    var c = _factory.CreateClient();
    var author = await Post(c, "author", new { name = "A4" });
    var tag = await Post(c, "tag", new { name = "CSharpTag" });
    var hit = await Post(c, "article", new { title = "TAGGED", status = "draft", authorId = author, tags = new[] { tag } });
    await Post(c, "article", new { title = "UNTAGGED", status = "draft", authorId = author });

    var envelope = JsonSerializer.SerializeToElement(new
    {
        filter = new Dictionary<string, object> { ["tags.name"] = Eq("CSharpTag") }
    });
    var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
    var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
    ids.Should().Contain(hit);
    foreach (var r in data.EnumerateArray())
        r.GetProperty("title").GetString().Should().Be("TAGGED");
}

[Fact]
public async Task Filter_o2m_category_by_article_title()
{
    var c = _factory.CreateClient();
    var author = await Post(c, "author", new { name = "A5" });
    var cat = await Post(c, "category", new { name = "O2MFilterCat" });
    await Post(c, "article", new { title = "UniqueChildTitle", status = "draft", authorId = author, categoryId = cat });

    var envelope = JsonSerializer.SerializeToElement(new
    {
        filter = new Dictionary<string, object> { ["articles.title"] = Eq("UniqueChildTitle") }
    });
    var data = Root(await (await c.PostAsJsonAsync("/api/items/category/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
    data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).Should().Contain(cat);
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test --filter "FullyQualifiedName~CrossRelationFilterTests"`. Expected: the two new tests FAIL (`HopAsync` throws "not implemented yet" for O2M/M2M).

- [ ] **Step 3: Implement the O2M and M2M hops** — replace the `default:` arm in `HopAsync` with:

```csharp
case RelationKind.OneToMany:
{
    // target(child) rows whose id IN targetIds -> read the reverse FK -> declaring (parent) ids
    var children = await repository.QueryWhereInAsync(seg.Relation.TargetCollection, "id", targetIds, ct);
    return ReadIds(children, desc.ReverseForeignKeyProperty!);
}
case RelationKind.ManyToMany:
{
    // junction rows whose targetFk IN targetIds -> read parentFk -> declaring ids
    var junctions = await repository.QueryEntityWhereInAsync(desc.JunctionType!, desc.JunctionTargetFk!, targetIds, ct);
    return ReadIds(junctions, desc.JunctionParentFk!);
}
default:
    throw new QueryException($"Unsupported relation kind '{seg.Relation.Kind}'.");
```

- [ ] **Step 4: Run the new tests** — `dotnet test --filter "FullyQualifiedName~CrossRelationFilterTests"`. Expected: PASS (all filter cases, to-one + to-many).

- [ ] **Step 5: Run the full suite** — `dotnet test`. Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Infrastructure/Query/RelationFilterResolver.cs tests/Struo.Tests/Query/CrossRelationFilterTests.cs
git commit -m "feat: to-many EXISTS (O2M + M2M) cross-relation filters"
```

---

### Task 5: Cross-relation sort (to-one paths) in `BuildOrderBy`

**Goal:** Sort on a to-one relation path (`sort=-category.name`), using the mechanism chosen by the Task 1 spike. Sort across a to-many segment is already rejected at validation (Task 2); this task adds an integration test confirming the 400.

**Files:**
- Modify: `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` (`BuildOrderBy` + ctor: inject `RelationshipGraph` + `IMetadataProvider`)
- Modify: the Infrastructure DI registration if the repository ctor signature changes (usually a no-op with DI)
- Test: `tests/Struo.Tests/Query/CrossRelationSortTests.cs` (new)

**Interfaces:**
- Consumes: `RelationshipGraph` (descriptors / `Resolve`), `IMetadataProvider`, `db.EntityMaintenance.GetTableName` / `GetDbColumnName`, `RelationPath`.
- Produces: `BuildOrderBy` handles dotted to-one sort fields.

- [ ] **Step 1: Write the failing integration test** — `tests/Struo.Tests/Query/CrossRelationSortTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class CrossRelationSortTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<long> Post(System.Net.Http.HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

    [Fact]
    public async Task Sort_descending_by_category_name()
    {
        var c = _factory.CreateClient();
        var author = await Post(c, "author", new { name = "S1" });
        var catA = await Post(c, "category", new { name = "AAA_sort" });
        var catZ = await Post(c, "category", new { name = "ZZZ_sort" });
        var artA = await Post(c, "article", new { title = "sortA", status = "draft", authorId = author, categoryId = catA });
        var artZ = await Post(c, "article", new { title = "sortZ", status = "draft", authorId = author, categoryId = catZ });

        // sort=-category.name should put the ZZZ-category article before the AAA-category one.
        var envelope = JsonSerializer.SerializeToElement(new { sort = new[] { "-category.name" } });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query", envelope)).Content.ReadAsStringAsync()).GetProperty("data");
        var order = data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).ToList();
        order.IndexOf(artZ).Should().BeLessThan(order.IndexOf(artA));
    }

    [Fact]
    public async Task Sort_across_to_many_returns_400()
    {
        var c = _factory.CreateClient();
        var envelope = JsonSerializer.SerializeToElement(new { sort = new[] { "-tags.name" } });
        (await c.PostAsJsonAsync("/api/items/article/query", envelope)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

> Confirm the envelope `sort` shape against `QueryParserTests.cs` (string array vs single string with `-` prefix for descending). Match the parser; do not change it.

- [ ] **Step 2: Run to verify it fails** — `dotnet test --filter "FullyQualifiedName~CrossRelationSortTests"`. Expected: `Sort_descending_by_category_name` FAILS (dotted sort column not built); the 400 test may already pass from Task 2 validation — keep it as a guard.

- [ ] **Step 3: Implement dotted sort in `BuildOrderBy`** — inject `RelationshipGraph graph` and `IMetadataProvider metadata` into the `SqlSugarItemRepository` primary constructor, and change `BuildOrderBy` to detect dotted sort fields. **Use the mechanism the Task 1 spike chose.** Primary (correlated-subquery-string) variant:

```csharp
private string? BuildOrderBy(IReadOnlyList<SortField> sort, EntityDescriptor d, string collection)
{
    if (sort.Count == 0) return null;
    var parts = sort.Select(s =>
    {
        if (RelationPath.IsRelationPath(s.Field))
            return $"{RelationOrderExpr(collection, s.Field)} {(s.Descending ? "DESC" : "ASC")}";
        var prop = d.FieldToProperty.TryGetValue(s.Field, out var p) ? p : s.Field;
        var col = db.EntityMaintenance.GetDbColumnName(prop, d.EntityType);
        return $"{col} {(s.Descending ? "DESC" : "ASC")}";
    });
    return string.Join(", ", parts);
}

/// <summary>
/// Builds a correlated-subquery ORDER BY expression for a to-one relation path. Validated
/// to be all-to-one by QueryValidator before reaching here. Built inside-out so multi-level
/// paths nest: category.parent.name -> (SELECT name FROM category WHERE id =
/// (SELECT parent_id FROM category WHERE id = article.category_id)).
/// </summary>
private string RelationOrderExpr(string rootCollection, string path)
{
    var rp = RelationPath.Parse(rootCollection, path, graph, metadata, options.MaxRelationDepth);
    var rootDesc = registry.Get(rootCollection)!;
    var rootTable = db.EntityMaintenance.GetTableName(rootDesc.EntityType);

    // innermost reference: rootTable.<fk column of the first segment>
    var first = rp.Segments[0];
    var firstFkClr = rootDesc.FieldToProperty.TryGetValue(first.Relation.ForeignKey!, out var fp)
        ? fp : Capitalize(first.Relation.ForeignKey!);
    var current = $"{rootTable}.{db.EntityMaintenance.GetDbColumnName(firstFkClr, rootDesc.EntityType)}";

    for (var i = 0; i < rp.Segments.Count; i++)
    {
        var seg = rp.Segments[i];
        var targetDesc = registry.Get(seg.Relation.TargetCollection)!;
        var targetTable = db.EntityMaintenance.GetTableName(targetDesc.EntityType);
        var idCol = db.EntityMaintenance.GetDbColumnName(targetDesc.IdProperty, targetDesc.EntityType);

        string selectExpr;
        if (i == rp.Segments.Count - 1)
        {
            var leafClr = targetDesc.FieldToProperty.TryGetValue(rp.LeafField, out var lp) ? lp : rp.LeafField;
            selectExpr = db.EntityMaintenance.GetDbColumnName(leafClr, targetDesc.EntityType);
        }
        else
        {
            var next = rp.Segments[i + 1];
            var nextFkClr = targetDesc.FieldToProperty.TryGetValue(next.Relation.ForeignKey!, out var np)
                ? np : Capitalize(next.Relation.ForeignKey!);
            selectExpr = db.EntityMaintenance.GetDbColumnName(nextFkClr, targetDesc.EntityType);
        }
        current = $"(SELECT {targetTable}.{selectExpr} FROM {targetTable} WHERE {targetTable}.{idCol} = {current})";
    }
    return current;
}

private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];
```

Update the `QueryAsync` call site to pass `collection`: `var orderBy = BuildOrderBy(query.Sort, d, collection);`.

> **Spike fallback (Task 1 chose LeftJoin instead):** drop `RelationOrderExpr` and, in `RunQueryAsync`, attach a `LeftJoin` for the sort path and `OrderBy` the joined column. Keep paging/`Total` correct (a to-one left join does not multiply rows). Use whichever the spike validated; delete the other.

- [ ] **Step 4: Run the new tests** — `dotnet test --filter "FullyQualifiedName~CrossRelationSortTests"`. Expected: PASS (descending order correct; to-many sort 400).

- [ ] **Step 5: Run the full suite** — `dotnet test`. Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs tests/Struo.Tests/Query/CrossRelationSortTests.cs
git commit -m "feat: to-one cross-relation sort via correlated subquery"
```

---

### Task 6: Live PostgreSQL verification + Phase-3b gate

**Goal:** Exercise the cross-relation queries against real PostgreSQL (§11 verification gate) and confirm the full Phase-3b acceptance criteria.

**Files:**
- None (verification task). Optionally update `docs/guide/01-getting-started.md` if query-DSL docs mention relation paths.

**Interfaces:** none.

- [ ] **Step 1: Confirm SQLite suite is green** — `dotnet test`. Record the pass count.

- [ ] **Step 2: Start the API against dev PostgreSQL.** Use the project's normal run path (the dev connection string in `appsettings.Development.json` / user-secrets). Confirm it starts cleanly (InitTables creates the Blog tables).

- [ ] **Step 3: Exercise each capability over HTTP against Postgres** (curl or the `! <cmd>` session helper):
  - to-one filter: `POST /api/items/article/query` with `{"filter":{"category.name":{"_eq":"<seeded>"}}}` → only matching rows.
  - multi-level filter: `{"filter":{"category.parent.name":{"_eq":"<seeded>"}}}`.
  - to-many EXISTS: `{"filter":{"tags.name":{"_eq":"<seeded>"}}}`.
  - to-one sort: `{"sort":["-category.name"]}` → correct order.
  - to-many sort rejected: `{"sort":["-tags.name"]}` → 400.
  - empty match: a non-existent value → `{"data":[]}`, HTTP 200.

- [ ] **Step 4: Record results in the report** (each query + observed result). This closes the §11 gate.

- [ ] **Step 5: Commit** (only if docs changed)

```bash
git add docs/guide/01-getting-started.md
git commit -m "docs: note cross-relation filter/sort in query DSL guide"
```

---

## Self-Review (controller, before execution)

- **Spec coverage:** to-one filter (T3) ✓; multi-level filter (T3) ✓; to-many EXISTS O2M+M2M (T4) ✓; to-one sort (T5) ✓; sort-over-to-many 400 (T2 validate + T5 integration) ✓; depth cap (T2) ✓; empty→`id IS NULL` (T3) ✓; AND/OR composition (T3) ✓; `MaxRelationDepth` reuse, no new option (T2/Global) ✓; live Postgres (T6) ✓; remove Phase-2 dotted rejection (T2) ✓.
- **Spike-first:** T1 settles the sort mechanism before T5; controller folds the outcome into T5 Step 3.
- **Type consistency:** `RelationPath.Parse(rootCollection, path, graph, metadata, maxDepth)`, `IsRelationPath`, `RelationSegment(RelationName, Relation, DeclaringCollection)`, `IRelationFilterResolver.RewriteAsync`, `IItemRepository.QueryIdsAsync` — used identically across T2–T5.
- **Known verify-against-codebase points (flagged inline):** exact `filter`/`_or`/`sort` envelope shape (verify vs `QueryParserTests`); `EntityDescriptor.FieldToProperty` member name; `RelationshipGraph.Descriptors(...)` item members (`ReverseForeignKeyProperty`, `JunctionType`, `JunctionParentFk`, `JunctionTargetFk`); whether the M2O FK is exposed as a CLR name on the descriptor (prefer it over `Capitalize`); SqlSugar main-table alias in correlated `ORDER BY` (the spike's crux).
