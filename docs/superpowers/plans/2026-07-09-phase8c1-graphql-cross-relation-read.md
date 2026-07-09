# Phase 8c.1 — GraphQL cross-relation read Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose cross-relation (dotted-path) filtering over many-to-one relations (multi-hop) on the GraphQL read API, and lock in cross-relation sorting with tests + docs — by reusing the existing REST-side engine and validation.

**Architecture:** Pure `Struo.Api/GraphQl` change plus making `FilterInputTranslator` metadata-aware. A nested typed relation filter input (`filter: { category: { name: { eq } } }`) is flattened by the translator into a dotted `ComparisonFilter("category.name", …)` that the **unchanged** `QueryValidator` → `RelationFilterResolver` already handles (rewrites to `id IN (…)`). Sort dotted tokens already thread through untyped; this slice verifies/tests/documents them. Domain / Application / Infrastructure are untouched; no new packages.

**Tech Stack:** .NET 10, C# latest, HotChocolate v16 (dynamic schema via `ITypeModule`), xUnit + AwesomeAssertions, SqlSugarCore (unchanged), PostgreSQL (live gate) / SQLite (existing engine tests).

## Global Constraints

- **Dependency rule (§2):** change stays in `Struo.Api`; no edits to `Struo.Domain` / `Struo.Application` / `Struo.Infrastructure`; framework code never references `samples/*`.
- **No new packages (§17.5):** `Directory.Packages.props` unchanged.
- **Build gate:** `dotnet build -warnaserror` must stay at **0 warnings**; `dotnet test` all green (baseline **513**, count rises with new tests).
- **Outbound JSON = camelCase**; GraphQL field/relation names are the metadata `Name` values (camelCase already).
- **Scope fence:** M2O relations only. No O2M/M2M cross-relation filter, no nested-list arguments, no depth>1 relation expansion (all → Phase 8c.2).
- **TDD:** failing test first; keep all existing tests green as characterization (the translator change is additive — own-field-only filters must behave identically).
- **Test scope division (read before writing tests):** the GraphQL execution tests use `FakeGraphQlDataSource` as a **spy** that captures the `QueryModel` and bypasses `ItemService`/`QueryValidator`/`RelationFilterResolver`. Therefore execution tests assert **translation wiring only** (that a dotted `FieldPath` / sort token is produced and passed through). Actual filtering, path validation, and negative cases (unknown relation → `BAD_USER_INPUT`, over-depth, to-many sort rejection) are already covered by the Query-layer tests (`tests/Struo.Tests/Query/CrossRelationFilterTests.cs`, `QueryValidator`/`RelationPath` tests) and are re-confirmed end-to-end at the **live PG gate**. Do NOT try to assert `BAD_USER_INPUT` or row filtering through the fake harness.

---

### Task 1: Metadata-aware translator (dotted-path flattening)

The one piece of genuinely new logic. `FilterInputTranslator.Translate` currently produces flat `ComparisonFilter`s. Add a metadata-aware overload that descends M2O relation-named keys, prefixing inner field paths with `"<relation>."` — recursively, so multi-hop composes.

**Before you start:** open `src/Struo.Domain/Query/FilterNode.cs` and confirm the record shapes used below: `ComparisonFilter(string FieldPath, QueryOperator Op, object? Value)` (positional record → supports `with { FieldPath = … }`) and `LogicalFilter(LogicalOperator Op, IReadOnlyList<FilterNode> Children)`. The `Prefix` helper relies on these being immutable records.

**Files:**
- Modify: `src/Struo.Api/GraphQl/FilterInputTranslator.cs`
- Test: `tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs`

**Interfaces:**
- Consumes: `FilterNode`, `ComparisonFilter`, `LogicalFilter`, `LogicalOperator`, `QueryOperator` (from `Struo.Domain.Query`).
- Produces (later tasks rely on these exact signatures):
  - `FilterInputTranslator.Translate(IReadOnlyDictionary<string, object?>? filter)` — **unchanged 1-arg overload**, flat-only (no relation descent).
  - `FilterInputTranslator.Translate(IReadOnlyDictionary<string, object?>? filter, string collection, Func<string, string, string?>? relationTarget)` — **new**. `relationTarget(collection, key)` returns the target collection name if `key` is an M2O relation of `collection`, else `null`. When `relationTarget` is `null`, behaves exactly like the 1-arg overload.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs` (inside the class):

```csharp
    // relationTarget stub: "category" and "parent" are M2O relations whose target is "category"
    // (mirrors the sample Article.category and Category.parent self-relation). Everything else is a
    // plain field. Independent of real fixtures/DB.
    private static readonly Func<string, string, string?> Rel =
        (_, key) => key is "category" or "parent" ? "category" : null;

    [Fact]
    public void Nested_relation_becomes_dotted_comparison()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["category"] = new Dictionary<string, object?>
            {
                ["name"] = new Dictionary<string, object?> { ["eq"] = "Tech" }
            }
        }, "article", Rel);

        var cmp = f.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("category.name");
        cmp.Op.Should().Be(QueryOperator.Eq);
        cmp.Value.Should().Be("Tech");
    }

    [Fact]
    public void Multi_hop_relation_becomes_multi_dotted_comparison()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["category"] = new Dictionary<string, object?>
            {
                ["parent"] = new Dictionary<string, object?>
                {
                    ["name"] = new Dictionary<string, object?> { ["eq"] = "Root" }
                }
            }
        }, "article", Rel);

        var cmp = f.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("category.parent.name");
        cmp.Value.Should().Be("Root");
    }

    [Fact]
    public void Own_field_and_nested_relation_are_anded()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["status"] = new Dictionary<string, object?> { ["eq"] = "published" },
            ["category"] = new Dictionary<string, object?>
            {
                ["name"] = new Dictionary<string, object?> { ["eq"] = "Tech" }
            }
        }, "article", Rel);

        var logical = f.Should().BeOfType<LogicalFilter>().Subject;
        logical.Op.Should().Be(LogicalOperator.And);
        logical.Children.Should().HaveCount(2);
        logical.Children.OfType<ComparisonFilter>().Select(c => c.FieldPath)
            .Should().Contain(new[] { "status", "category.name" });
    }

    [Fact]
    public void Or_group_with_a_nested_relation_child_is_honoured()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["or"] = new List<object?>
            {
                new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "a" } },
                new Dictionary<string, object?>
                {
                    ["category"] = new Dictionary<string, object?>
                    {
                        ["name"] = new Dictionary<string, object?> { ["eq"] = "Tech" }
                    }
                },
            }
        }, "article", Rel);

        var logical = f.Should().BeOfType<LogicalFilter>().Subject;
        logical.Op.Should().Be(LogicalOperator.Or);
        logical.Children.OfType<ComparisonFilter>().Select(c => c.FieldPath)
            .Should().Contain("category.name");
    }

    [Fact]
    public void Null_operators_inside_nested_relation_are_skipped()
    {
        // HotChocolate backfills every declared operator field as null; only "eq" was set.
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["category"] = new Dictionary<string, object?>
            {
                ["name"] = new Dictionary<string, object?>
                {
                    ["eq"] = "Tech", ["neq"] = null, ["contains"] = null, ["in"] = null
                }
            }
        }, "article", Rel);

        f.Should().BeOfType<ComparisonFilter>().Which.FieldPath.Should().Be("category.name");
    }

    [Fact]
    public void One_arg_overload_still_treats_relation_key_as_flat_field()
    {
        // Back-compat: with no relationTarget, "category" is NOT a relation, so its dict is read as
        // an operator bag; "name" is not a known operator -> nothing emitted -> null.
        FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["category"] = new Dictionary<string, object?>
            {
                ["name"] = new Dictionary<string, object?> { ["eq"] = "Tech" }
            }
        }).Should().BeNull();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~FilterInputTranslatorTests`
Expected: the six new tests FAIL (compile error on the 3-arg overload, then assertion failures); the existing tests still pass.

- [ ] **Step 3: Implement the metadata-aware overload**

Rewrite `src/Struo.Api/GraphQl/FilterInputTranslator.cs` so `Translate` gains the 3-arg overload, adds the relation-descent branch, and threads `collection`/`relationTarget` through `AddGroup`. Keep `AddField`, `AsDict`, `OperatorTokens`, `OperatorInputTypeName`, `NumberFilter`, and the null-op skip **verbatim**. Add the immutable `Prefix` helper.

```csharp
// src/Struo.Api/GraphQl/FilterInputTranslator.cs
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Translates a submitted GraphQL filter input (read back as a nested dictionary because the
/// input type's RuntimeType is a dictionary) into the existing <see cref="FilterNode"/> tree,
/// so the whole query then flows through the existing QueryValidator + repository unchanged.
/// Cross-relation filtering: a key naming an M2O relation carries a nested filter dict, which is
/// flattened into dotted <see cref="ComparisonFilter"/> field paths (e.g. "category.name",
/// "category.parent.name") that RelationFilterResolver already rewrites to `id IN (…)`.
/// </summary>
public static class FilterInputTranslator
{
    public static readonly IReadOnlyDictionary<string, QueryOperator> OperatorTokens =
        new Dictionary<string, QueryOperator>(StringComparer.Ordinal)
        {
            ["eq"] = QueryOperator.Eq, ["neq"] = QueryOperator.Neq,
            ["in"] = QueryOperator.In, ["nin"] = QueryOperator.Nin,
            ["lt"] = QueryOperator.Lt, ["lte"] = QueryOperator.Lte,
            ["gt"] = QueryOperator.Gt, ["gte"] = QueryOperator.Gte,
            ["contains"] = QueryOperator.Contains,
            ["startsWith"] = QueryOperator.StartsWith,
            ["endsWith"] = QueryOperator.EndsWith,
        };

    public static string OperatorInputTypeName(FieldInterface iface, Type? clrType) => iface switch
    {
        FieldInterface.Number or FieldInterface.Slider or FieldInterface.Rating => NumberFilter(clrType),
        FieldInterface.Boolean or FieldInterface.Checkbox => "BooleanFilter",
        FieldInterface.DateTime or FieldInterface.Date => "DateTimeFilter",
        FieldInterface.File or FieldInterface.Image or FieldInterface.Uuid => "IdFilter",
        _ => "StringFilter"
    };

    private static string NumberFilter(Type? clrType)
    {
        var t = clrType is null ? null : Nullable.GetUnderlyingType(clrType) ?? clrType;
        return (t == typeof(int) || t == typeof(short) || t == typeof(byte) || t == typeof(long))
            ? "IntFilter" : "FloatFilter";
    }

    /// <summary>Flat-only translation (no cross-relation descent). Back-compatible entry point.</summary>
    public static FilterNode? Translate(IReadOnlyDictionary<string, object?>? filter)
        => Translate(filter, collection: "", relationTarget: null);

    /// <summary>
    /// Metadata-aware translation. <paramref name="relationTarget"/> returns the target collection
    /// name when a key is an M2O relation of <paramref name="collection"/>, else null; when it is
    /// null this behaves exactly like the flat overload.
    /// </summary>
    public static FilterNode? Translate(
        IReadOnlyDictionary<string, object?>? filter,
        string collection,
        Func<string, string, string?>? relationTarget)
    {
        if (filter is null || filter.Count == 0) return null;
        var children = new List<FilterNode>();

        foreach (var (key, value) in filter)
        {
            if (value is null) continue;
            if (string.Equals(key, "and", StringComparison.Ordinal))
                AddGroup(children, value, LogicalOperator.And, collection, relationTarget);
            else if (string.Equals(key, "or", StringComparison.Ordinal))
                AddGroup(children, value, LogicalOperator.Or, collection, relationTarget);
            else if (relationTarget?.Invoke(collection, key) is { } target && AsDict(value) is { } nested)
            {
                // Cross-relation: translate the nested filter relative to the target collection,
                // then prefix every produced field path with "<relation>." (multi-hop stacks).
                if (Translate(nested, target, relationTarget) is { } node)
                    children.Add(Prefix(node, key + "."));
            }
            else
                AddField(children, key, value);
        }

        if (children.Count == 0) return null;
        return children.Count == 1 ? children[0] : new LogicalFilter(LogicalOperator.And, children);
    }

    private static FilterNode Prefix(FilterNode node, string prefix) => node switch
    {
        ComparisonFilter c => c with { FieldPath = prefix + c.FieldPath },
        LogicalFilter l => new LogicalFilter(l.Op, l.Children.Select(ch => Prefix(ch, prefix)).ToList()),
        _ => node
    };

    private static void AddGroup(
        List<FilterNode> into, object value, LogicalOperator op,
        string collection, Func<string, string, string?>? relationTarget)
    {
        if (value is not System.Collections.IEnumerable list) return;
        var group = new List<FilterNode>();
        foreach (var item in list)
            if (AsDict(item) is { } d && Translate(d, collection, relationTarget) is { } node) group.Add(node);
        if (group.Count > 0) into.Add(new LogicalFilter(op, group));
    }

    private static void AddField(List<FilterNode> into, string field, object value)
    {
        if (AsDict(value) is not { } ops) return;
        foreach (var (token, opValue) in ops)
        {
            // HotChocolate reads the nested op-input (e.g. StringFilter) back as a dictionary
            // populated with EVERY declared field, not just the ones the client set — unset
            // operators default to null here. Skip them, or a single `{ eq: x }` filter would
            // explode into one ComparisonFilter per operator (neq/in/contains/... all value=null),
            // ANDed together and silently corrupting the query.
            if (opValue is null) continue;

            if (string.Equals(token, "isNull", StringComparison.Ordinal))
            {
                var isNull = opValue is true;
                into.Add(new ComparisonFilter(field, isNull ? QueryOperator.Null : QueryOperator.NNull, null));
            }
            else if (OperatorTokens.TryGetValue(token, out var qop))
            {
                into.Add(new ComparisonFilter(field, qop, opValue));
            }
        }
    }

    private static IReadOnlyDictionary<string, object?>? AsDict(object? o)
    {
        if (o is IReadOnlyDictionary<string, object?> ro) return ro;
        if (o is IDictionary<string, object?> d) return new Dictionary<string, object?>(d);
        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~FilterInputTranslatorTests`
Expected: PASS (all — the six new plus every pre-existing translator test, unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl/FilterInputTranslator.cs tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs
git commit -m "feat(graphql): metadata-aware filter translator flattens M2O relations to dotted paths (8c.1)"
```

---

### Task 2: Thread collection + relation resolver through BuildQuery and resolvers

Wire the translator's new overload into the read path. `BuildQuery` gains two **optional** params (back-compatible), and `CollectionResolvers` builds the `relationTarget` delegate from `IMetadataProvider` and passes it at both call sites.

**Files:**
- Modify: `src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs`
- Modify: `src/Struo.Api/GraphQl/CollectionResolvers.cs`
- Test: `tests/Struo.Tests/GraphQl/GraphQlQueryBuilderTests.cs`

**Interfaces:**
- Consumes: `FilterInputTranslator.Translate(filter, collection, relationTarget)` (Task 1); `IMetadataProvider.GetCollection(name)` → `CollectionMetadata { Relations: RelationMetadata[] }` where `RelationMetadata { Name, Kind, TargetCollection, ForeignKey }`; `RelationKind.ManyToOne` (from `Struo.Domain.Metadata.Enums`).
- Produces:
  - `GraphQlQueryBuilder.BuildQuery(filter, sort, limit, offset, search, requestedRelations, string collection = "", Func<string,string,string?>? relationTarget = null)`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Struo.Tests/GraphQl/GraphQlQueryBuilderTests.cs` (inside the class):

```csharp
    [Fact]
    public void BuildQuery_flattens_nested_M2O_relation_filter_to_dotted_path()
    {
        Func<string, string, string?> rel = (_, key) => key == "category" ? "category" : null;

        var q = GraphQlQueryBuilder.BuildQuery(
            filter: new Dictionary<string, object?>
            {
                ["category"] = new Dictionary<string, object?>
                {
                    ["name"] = new Dictionary<string, object?> { ["eq"] = "Tech" }
                }
            },
            sort: null, limit: null, offset: null, search: null,
            requestedRelations: System.Array.Empty<string>(),
            collection: "article", relationTarget: rel);

        var cmp = q.Filter.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("category.name");
        cmp.Value.Should().Be("Tech");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~GraphQlQueryBuilderTests`
Expected: the new test FAILS — currently `BuildQuery` calls `Translate(filter)` (flat), so `category` is read as an operator bag and `q.Filter` is `null` (or not a `ComparisonFilter`). Existing `GraphQlQueryBuilderTests` still pass (optional params keep their calls valid).

- [ ] **Step 3: Implement — optional params on BuildQuery + resolver factory + call sites**

In `src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs`, change `BuildQuery`'s signature and the `Translate` call:

```csharp
    public static QueryModel BuildQuery(
        IReadOnlyDictionary<string, object?>? filter,
        IReadOnlyList<string>? sort,
        int? limit,
        int? offset,
        string? search,
        IReadOnlyList<string> requestedRelations,
        string collection = "",
        Func<string, string, string?>? relationTarget = null)
    {
        var deep = requestedRelations.Count == 0
            ? null
            : new DeepSpec(requestedRelations.ToDictionary(
                r => r, _ => new DeepRelationSpec(null, null), StringComparer.OrdinalIgnoreCase));

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

In `src/Struo.Api/GraphQl/CollectionResolvers.cs`, add the enums using at the top:

```csharp
using Struo.Domain.Metadata.Enums;
```

Add a resolver-factory helper (place it near `SelectionRelations`):

```csharp
    /// <summary>
    /// Delegate for FilterInputTranslator: returns the target collection name when <paramref name="key"/>
    /// is an M2O relation of <paramref name="coll"/> (so a nested filter descends into a dotted path),
    /// else null. Only M2O relations with a foreign key participate (parity with the schema input).
    /// </summary>
    private static Func<string, string, string?> RelationTargets(IMetadataProvider metadata) =>
        (coll, key) => metadata.GetCollection(coll)?.Relations
            .FirstOrDefault(r => r.Kind == RelationKind.ManyToOne && r.ForeignKey is not null
                              && string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase))
            ?.TargetCollection;
```

Update `ResolveList` to pass the collection + resolver:

```csharp
        var relations = SelectionRelations(ctx, collection, elementIsDirect: false);
        var query = GraphQlQueryBuilder.BuildQuery(
            filter, sort, limit, offset, search, relations,
            collection, RelationTargets(ctx.Service<IMetadataProvider>()));
```

Update `ResolveSingle` similarly (filter is always null there, but pass them for uniformity):

```csharp
        var relations = SelectionRelations(ctx, collection, elementIsDirect: true);
        var deep = GraphQlQueryBuilder.BuildQuery(
            null, null, null, null, null, relations,
            collection, RelationTargets(ctx.Service<IMetadataProvider>())).Deep;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~GraphQlQueryBuilderTests`
Expected: PASS (new + existing).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs src/Struo.Api/GraphQl/CollectionResolvers.cs tests/Struo.Tests/GraphQl/GraphQlQueryBuilderTests.cs
git commit -m "feat(graphql): thread collection + M2O relation resolver into read query builder (8c.1)"
```

---

### Task 3: Schema — emit M2O relation filter field (+ fixture self-relation)

Add, to every collection's `{X}FilterInput`, one nested filter field per M2O relation, typed as the target's `{Target}FilterInput` (by name → recursive/self-referential like `and`/`or`). Extend the test fixture's `category` with a `parent` M2O self-relation so the multi-hop / self-reference cases are exercisable.

**Files:**
- Modify: `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs` (`BuildFilterInput`)
- Modify: `tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs`
- Test: `tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs`

**Interfaces:**
- Consumes: `RelationMetadata { Name, Kind, TargetCollection, ForeignKey }`; `SchemaTypeMapper.TypeName(collection)`; `RelationKind.ManyToOne`.
- Produces (schema shape later tasks rely on): `ArticleFilterInput` has field `category: CategoryFilterInput` (and keeps `categoryId: IdFilter`); `CategoryFilterInput` has field `parent: CategoryFilterInput` (self-ref) and `name: StringFilter`.

- [ ] **Step 1: Extend the fixture with a Category.parent M2O self-relation**

In `tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs`, in `Category()`'s `Relations` collection, add the `parent` relation **before** `articles`:

```csharp
            new RelationMetadata
            {
                Name = "parent", Label = "Parent", Kind = RelationKind.ManyToOne,
                TargetCollection = "category", Interface = RelationInterface.TreeSelect,
                ForeignKey = "parentId", DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull,
            },
```

Add a `ParentId` property to `CategoryPoco`:

```csharp
    private sealed class CategoryPoco
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public Guid? ParentId { get; set; }
    }
```

Add the `parentId` FK to the registry's `category` descriptor `FieldToProperty` map (after `["name"] = "Name",`):

```csharp
                        ["parentId"] = "ParentId",
```

- [ ] **Step 2: Write the failing schema tests**

Append to `tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs` (inside the class; match the existing schema-building helper in that file — it exposes the built `ISchema`. If the helper is named differently, reuse whatever the existing tests call; the assertions below only need the built schema).

```csharp
    [Fact]
    public async Task ArticleFilterInput_has_nested_category_relation_filter()
    {
        var schema = await BuildSchemaAsync();
        var input = schema.Types.OfType<HotChocolate.Types.IInputObjectType>()
            .Single(t => t.Name == "ArticleFilterInput");

        input.Fields.Any(f => f.Name == "category" && f.Type.NamedType().Name == "CategoryFilterInput")
            .Should().BeTrue();
        // FK operator field is retained (parity with REST allowlist).
        input.Fields.Any(f => f.Name == "categoryId" && f.Type.NamedType().Name == "IdFilter")
            .Should().BeTrue();
    }

    [Fact]
    public async Task CategoryFilterInput_is_self_referential_via_parent()
    {
        var schema = await BuildSchemaAsync();
        var input = schema.Types.OfType<HotChocolate.Types.IInputObjectType>()
            .Single(t => t.Name == "CategoryFilterInput");

        input.Fields.Any(f => f.Name == "parent" && f.Type.NamedType().Name == "CategoryFilterInput")
            .Should().BeTrue();
        input.Fields.Any(f => f.Name == "name" && f.Type.NamedType().Name == "StringFilter")
            .Should().BeTrue();
    }
```

> If `GraphQlSchemaTests` does not already expose a `BuildSchemaAsync()` returning `ISchema`, add a private static helper mirroring the executor setup in `GraphQlExecutionTests.ExecutorAsync` but ending in `.BuildSchemaAsync()` instead of `.BuildRequestExecutorAsync()`. Check the file first and reuse its existing helper name.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~GraphQlSchemaTests`
Expected: the two new tests FAIL — `category`/`parent` relation filter fields are not emitted yet.

- [ ] **Step 4: Implement — emit the relation filter field**

In `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`, replace the M2O FK loop in `BuildFilterInput` (currently lines ~209-212):

```csharp
        // M2O foreign keys are filterable (parity with REST allowlist); each M2O relation also gets a
        // nested filter input typed as the target's FilterInput (by name -> recursive/self-referential,
        // same mechanism as and/or). Flattened to a dotted FieldPath by FilterInputTranslator.
        foreach (var rel in meta.Relations)
            if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
            {
                config.Fields.Add(new InputFieldConfiguration(fk, null, TypeReference.Parse("IdFilter")));
                var targetFilter = SchemaTypeMapper.TypeName(rel.TargetCollection) + "FilterInput";
                config.Fields.Add(new InputFieldConfiguration(rel.Name, null, TypeReference.Parse(targetFilter)));
            }
```

- [ ] **Step 5: Run tests to verify they pass, then the full GraphQl suite (ripple guard)**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~GraphQlSchemaTests`
Expected: PASS.

Then guard against fixture ripple (the new `parent` field on `Category`/`CategoryFilterInput`):

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~Struo.Tests.GraphQl`
Expected: PASS (all GraphQl tests). If a pre-existing test asserted an exhaustive field set on `Category`/`CategoryFilterInput`, update it to include `parent`/`parentId` (these are legitimately new schema fields from the fixture's new relation).

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs
git commit -m "feat(graphql): emit nested M2O relation filter inputs (self-referential, multi-hop) (8c.1)"
```

---

### Task 4: Execution — nested cross-relation filter flows end-to-end (spy)

Prove, through the real dynamic schema against the spy data source, that a nested GraphQL relation filter parses and arrives at the data source as a dotted `ComparisonFilter`. No `src` change — this is the acceptance gate for Tasks 1–3 combined.

**Files:**
- Test: `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs`

**Interfaces:**
- Consumes: `GraphQlExecutionTests.ExecutorAsync(FakeGraphQlDataSource)` and `ParseData(...)` (existing helpers in the file); `FakeGraphQlDataSource.OnQuery` capture pattern (see the existing `List_passes_filter_sort_pagination_into_QueryModel` test).

- [ ] **Step 1: Write the tests (expected to pass once 1–3 are in)**

Append to `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task Nested_M2O_relation_filter_arrives_as_dotted_comparison()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
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
            OnQuery = (_, q, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
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
            OnQuery = (_, q, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(filter: { status: { eq: \"published\" }, category: { name: { eq: \"Tech\" } } }) { total } }");
        ParseData(result);

        var logical = captured!.Filter.Should().BeOfType<LogicalFilter>().Subject;
        logical.Children.OfType<ComparisonFilter>().Select(c => c.FieldPath)
            .Should().Contain(new[] { "status", "category.name" });
    }
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~GraphQlExecutionTests`
Expected: PASS (these validate the full schema→resolver→builder→translator wiring). A failure here means a bug in Tasks 1–3 — fix there, not by weakening the test.

- [ ] **Step 3: Commit**

```bash
git add tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs
git commit -m "test(graphql): nested + multi-hop M2O cross-relation filter flows to dotted path (8c.1)"
```

---

### Task 5: Cross-relation sort test + docs + verification gate

Lock in cross-relation sort (already threads through untyped) with an execution spy test, then update ROADMAP + guide with usage examples and the D9 caveat. Finish with the full-suite green gate.

**Files:**
- Test: `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs`
- Modify: `docs/ROADMAP.md`
- Modify: `docs/guide/*` (the GraphQL usage guide, if present; otherwise add a short section to the Phase 8 guide/README that documents cross-relation querying)

**Interfaces:**
- Consumes: the same `ExecutorAsync`/`OnQuery` spy pattern.

- [ ] **Step 1: Write the cross-relation sort test**

Append to `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs`:

```csharp
    [Fact]
    public async Task Cross_relation_sort_token_flows_into_QueryModel()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
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
```

- [ ] **Step 2: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~GraphQlExecutionTests`
Expected: PASS (sort tokens already thread through `ParseSort` → `QueryModel.Sort`; this documents/locks the behavior).

- [ ] **Step 3: Update ROADMAP + guide**

In `docs/ROADMAP.md`: mark Phase 8c.1 done (pending live gate), add a row/paragraph mirroring the existing phase entries (scope: M2O cross-relation filter multi-hop + cross-relation sort; engine reused; Api-only), and update the Phase 8c table row (`8c` currently "planned") to reflect the 8c.1 slice.

In the GraphQL usage guide (find it: `ls docs/guide` — likely a Phase 8 delivery-API guide; if none names GraphQL, append a "Cross-relation querying" section to the closest GraphQL doc), document:

````markdown
### Cross-relation filtering (many-to-one, multi-hop)

Filter a collection by a field on a many-to-one relation target by nesting the target's filter input:

```graphql
{ articles(filter: { category: { name: { eq: "Tech" } } }) { items { id } total } }
# multi-hop through an M2O chain:
{ articles(filter: { category: { parent: { name: { eq: "Root" } } } }) { items { id } total } }
```

Nested relation filters flatten to dotted field paths (`category.name`, `category.parent.name`) that
resolve to an `id IN (…)` condition on the queried collection.

### Cross-relation sorting (many-to-one, multi-hop)

```graphql
{ articles(sort: ["category.name", "-category.parent.name"]) { items { id } total } }
```

**Caveats:**
- Cross-relation filter/sort covers **many-to-one** relations only (to-many is a later slice).
- **Sort across a to-many relation is not supported** and returns `BAD_USER_INPUT`.
- Cross-relation **sort** builds a correlated ORDER-BY subquery whose SQL shape is PostgreSQL/SQLite-
  specific (audit D9); it is not verified on MySQL/SqlServer/Oracle.
- Relation paths are depth-capped by `StruoQueryOptions.MaxRelationDepth` (default 5); exceeding it,
  or naming an unknown relation/field, returns `BAD_USER_INPUT`.
````

- [ ] **Step 4: Full verification gate**

Run:
```bash
dotnet build -warnaserror
dotnet test tests/Struo.Tests
```
Expected: build 0 warnings; all tests green (baseline 513 + the new 8c.1 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs docs/ROADMAP.md docs/guide
git commit -m "test+docs(graphql): cross-relation sort spy test + usage/caveat docs (8c.1)"
```

---

## Live gate (run after implementation, real PostgreSQL — user/session driven)

SQLite-green ≠ Postgres-correct. On the live dev API against real Postgres (`web-struo-cms-db`) + Redis,
via GraphQL at `/graphql` (bootstrap super-admin), verify with evidence recorded:

1. Seed categories with a parent chain (e.g. `Root` → `Tech`) + articles linked to `Tech`.
2. `{ articles(filter: { category: { name: { eq: "Tech" } } }) { items { id } total } }` returns only the linked article(s).
3. Multi-hop: `{ articles(filter: { category: { parent: { name: { eq: "Root" } } } }) { total } }` returns them too.
4. Sort: `{ articles(sort: ["category.name"]) { items { id } } }` and `["-category.name"]` order correctly.
5. **CJK**: a category named e.g. `科技` filtered via `{ category: { name: { eq: "科技" } } }` round-trips code-point-exact (verify by code point, per the live-verify-utf8 discipline — send UTF-8 via PowerShell/Invoke-RestMethod or a UTF-8 body file, not Big5 curl).
6. Negative: `{ articles(filter: { category: { nope: { eq: "x" } } } ) { total } }` (unknown target field) and an unknown relation → `BAD_USER_INPUT`; a to-many sort (`sort: ["tags.name"]`) → `BAD_USER_INPUT`.
7. Empty match returns an empty list (not an error).

Record each query + response. Fix any Postgres-only issue in a follow-up commit, then update ROADMAP to "live-verified".

---

## Self-Review

**Spec coverage:**
- §4 schema (M2O relation filter field) → Task 3. ✅
- §5 translator dotted flattening (multi-hop, and/or, null-skip) → Task 1. ✅
- §5.1 nested and/or rejected by existing guard → covered by the reuse (no new code); negative confirmed at live gate (Task-5 live gate §6) since the fake harness bypasses the validator (documented in Global Constraints). ✅
- §6 sort verify/test/document → Task 5 (test) + Task 5 Step 3 (docs). ✅
- §7 error mapping → inherited (StruoErrorFilter unchanged); negatives at live gate. ✅
- §8 security (inherited REST behavior) → documented in spec; no code. ✅
- §9 testing (unit/schema/integration/live) → Tasks 1,3,4,5 + live gate. ✅
- §10 acceptance (build -warnaserror, tests green, frontend untouched, live gate) → Task 5 Step 4 + live gate. ✅
- §11 files → all touched files appear in tasks. ✅

**Placeholder scan:** the only intentional "find it in the file" note is the `GraphQlSchemaTests` helper name in Task 3 Step 2 (guarded with an explicit fallback recipe). No TBD/TODO/vague steps. ✅

**Type consistency:** `Translate(filter, collection, relationTarget)`, `Func<string,string,string?>`, `RelationTargets(IMetadataProvider)`, `BuildQuery(..., collection = "", relationTarget = null)`, `RelationMetadata.{Name,Kind,TargetCollection,ForeignKey}`, `RelationKind.ManyToOne`, `ComparisonFilter.FieldPath` / `with`, `LogicalFilter.{Op,Children}` — consistent across tasks. ✅
