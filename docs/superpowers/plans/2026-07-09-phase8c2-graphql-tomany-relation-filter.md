# Phase 8c.2 — GraphQL to-many cross-relation filter (O2M/M2M) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose to-many (O2M/M2M) cross-relation dotted-path filtering on the GraphQL read API with ANY/EXISTS semantics, by relaxing an existing "M2O only" guard in two `Struo.Api/GraphQl` files — reusing the REST engine + validator unchanged.

**Architecture:** Two source edits in `Struo.Api/GraphQl`: (1) `CollectionSchemaBuilder.BuildFilterInput` emits a nested `{Target}FilterInput` field for O2M/M2M relations (M2O already does); (2) `CollectionResolvers.RelationTargets` recognises O2M/M2M keys so `FilterInputTranslator` descends into them and produces a dotted `FieldPath`. The dotted filter flows through the unchanged `QueryValidator` → `RelationFilterResolver.RewriteAsync` (whose O2M/M2M hops already exist). `Struo.Domain`/`Struo.Application`/`Struo.Infrastructure` are untouched.

**Tech Stack:** .NET 10 / C#, HotChocolate v16 (dynamic schema via `ITypeModule`), xUnit + AwesomeAssertions, SqlSugarCore (SQLite for the API integration harness, PostgreSQL at the live gate).

## Global Constraints

- **Dependency rule:** Api → Application + Infrastructure. Domain free of external packages. This slice edits **only** `src/Struo.Api/GraphQl/**` + tests + docs. No `Struo.Domain`/`Struo.Application`/`Struo.Infrastructure` source change; no `samples/*` change.
- **No new packages.** `Directory.Packages.props` unchanged.
- **Build:** `dotnet build -warnaserror` must be clean (0 warnings).
- **Test baseline:** backend `dotnet test` is **526** green pre-slice; must stay green and rise with the new tests. Frontend untouched (237, not run here).
- **Purely additive to existing behavior:** existing M2O and own-field filter tests must stay green as characterization (no behavior change for M2O-only / own-field-only filters).
- **ANY/EXISTS only:** a to-many hop means "at least one related row matches" — the only semantics the id-set walk produces. No ALL/NONE/count.
- **Sort unchanged:** cross-relation sort across to-many relations remains rejected (`QueryValidator`); this slice does not touch sort.
- **Solution file:** build/test via the repo's normal commands (there is no `.sln`; use the test project). Run tests with `dotnet test tests/Struo.Tests/Struo.Tests.csproj`.

---

## File structure

| File | Responsibility | Change |
|---|---|---|
| `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs` | Builds all GraphQL types per collection | Modify `BuildFilterInput` — emit nested `{Target}FilterInput` for O2M/M2M (Change 1) |
| `src/Struo.Api/GraphQl/CollectionResolvers.cs` | Root query resolvers + the relation-target delegate | Modify `RelationTargets` — recognise O2M/M2M keys (Change 2) |
| `tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs` | Schema-shape gate (introspect built SDL/types) | Add O2M/M2M nested-filter-field tests |
| `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs` | Spy execution gate (dotted path arrives in captured `QueryModel`) | Add to-many filter flow tests |
| `tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs` | Translator unit gate | Add a to-many-shaped characterization test (translator unchanged) |
| `tests/Struo.Tests/Query/CrossRelationFilterTests.cs` | REST engine integration gate (real engine, SQLite) | Restore the M2M cross-relation filter test deleted in Phase 5.5 |
| `docs/ROADMAP.md` | Phase index + status | Add the 8c.2 status row + mark 8c.2 done post-verify |

The fake metadata fixtures (`FakeMetadataFixtures.cs`) already declare `Article.tags` (M2M → `tag`) and `Category.articles` (O2M → `article`), so **no fixture change is needed**.

---

## Task 1: Schema — O2M/M2M relations emit a nested `{Target}FilterInput`

**Files:**
- Modify: `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs` (method `BuildFilterInput`)
- Test: `tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs`

**Interfaces:**
- Consumes: `CollectionMetadata.Relations` (each `RelationMetadata` has `Kind`, `Name`, `TargetCollection`, `ForeignKey`); `SchemaTypeMapper.TypeName(string)`; the by-name `TypeReference.Parse` recursion mechanism (as used for `and`/`or`).
- Produces: `{X}FilterInput` input objects that, for every O2M and M2M relation `r`, contain a field named `r.Name` of type `{TypeName(r.TargetCollection)}FilterInput`. M2O behavior (FK `IdFilter` field + nested filter) is unchanged.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs` (uses the existing `BuildSchemaAsync()` helper already in the file):

```csharp
    [Fact]
    public async Task ArticleFilterInput_has_nested_tags_m2m_relation_filter()
    {
        var schema = await BuildSchemaAsync();
        var input = schema.Types.OfType<HotChocolate.Types.IInputObjectTypeDefinition>()
            .Single(t => t.Name == "ArticleFilterInput");

        // M2M relation -> nested target FilterInput (ANY/EXISTS). M2M carries no FK column,
        // so there is only the nested filter field (no "tagsId").
        input.Fields.Any(f => f.Name == "tags" && f.Type.NamedType().Name == "TagFilterInput")
            .Should().BeTrue();
        // 8c.1 M2O fields are retained.
        input.Fields.Any(f => f.Name == "category" && f.Type.NamedType().Name == "CategoryFilterInput")
            .Should().BeTrue();
        input.Fields.Any(f => f.Name == "categoryId" && f.Type.NamedType().Name == "IdFilter")
            .Should().BeTrue();
    }

    [Fact]
    public async Task CategoryFilterInput_has_nested_articles_o2m_relation_filter()
    {
        var schema = await BuildSchemaAsync();
        var input = schema.Types.OfType<HotChocolate.Types.IInputObjectTypeDefinition>()
            .Single(t => t.Name == "CategoryFilterInput");

        // O2M relation -> nested target FilterInput. This also proves the Category <-> Article
        // cyclic input reference (CategoryFilterInput.articles -> ArticleFilterInput.category ->
        // CategoryFilterInput) resolves by name without a build loop (BuildSchemaAsync would throw).
        input.Fields.Any(f => f.Name == "articles" && f.Type.NamedType().Name == "ArticleFilterInput")
            .Should().BeTrue();
        // 8c.1 M2O self-reference is retained.
        input.Fields.Any(f => f.Name == "parent" && f.Type.NamedType().Name == "CategoryFilterInput")
            .Should().BeTrue();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~GraphQlSchemaTests"`
Expected: the two new tests FAIL (`tags`/`articles` fields absent — `Any(...)` returns false). The existing schema tests still PASS.

- [ ] **Step 3: Implement Change 1 in `BuildFilterInput`**

In `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`, replace the current M2O-only relation loop (the `foreach (var rel in meta.Relations) if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk) { … }` block near the end of `BuildFilterInput`) with:

```csharp
        // Cross-relation filter inputs. M2O contributes its FK column as a filterable IdFilter
        // (parity with the REST allowlist) PLUS a nested target FilterInput. O2M/M2M carry no FK
        // on this collection, so they contribute only the nested target FilterInput (ANY/EXISTS
        // via RelationFilterResolver's to-many hops). Referenced by name -> recursive /
        // self-referential / cyclic input types resolve like and/or (no build loop). Flattened to
        // a dotted FieldPath by FilterInputTranslator.
        foreach (var rel in meta.Relations)
        {
            string? targetFilter = null;
            if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
            {
                config.Fields.Add(new InputFieldConfiguration(fk, null, TypeReference.Parse("IdFilter")));
                targetFilter = SchemaTypeMapper.TypeName(rel.TargetCollection) + "FilterInput";
            }
            else if (rel.Kind is RelationKind.OneToMany or RelationKind.ManyToMany)
            {
                targetFilter = SchemaTypeMapper.TypeName(rel.TargetCollection) + "FilterInput";
            }

            if (targetFilter is not null)
                config.Fields.Add(new InputFieldConfiguration(rel.Name, null, TypeReference.Parse(targetFilter)));
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~GraphQlSchemaTests"`
Expected: all `GraphQlSchemaTests` PASS (new + existing). The Category↔Article cycle resolving proves no build loop.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs
git commit -m "feat(graphql): emit nested filter input for O2M/M2M relations (8c.2 schema)"
```

---

## Task 2: Resolver delegate + end-to-end dotted-path flow

**Files:**
- Modify: `src/Struo.Api/GraphQl/CollectionResolvers.cs` (method `RelationTargets`)
- Test: `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs`, `tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs`

**Interfaces:**
- Consumes: the Task-1 schema fields (`tags`/`articles` nested filter inputs make the queries valid); `IMetadataProvider.GetCollection(...).Relations`; `FilterInputTranslator.Translate(filter, collection, relationTarget)` (unchanged — it is relation-kind-agnostic and asks the delegate for the target).
- Produces: `RelationTargets` returns the target collection name for a key that is an M2O-with-FK **or** an O2M **or** an M2M relation of the given collection, else null — so a nested to-many filter dict becomes a dotted `ComparisonFilter` (e.g. `tags.name`, `articles.status`) in `QueryModel.Filter`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs` (mirrors the existing `Nested_M2O_relation_filter_arrives_as_dotted_comparison` spy pattern):

```csharp
    [Fact]
    public async Task M2M_relation_filter_arrives_as_dotted_comparison()
    {
        QueryModel? captured = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
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
            OnQuery = (_, q, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
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
            OnQuery = (_, q, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
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
            OnQuery = (_, q, _) => { captured = q; return new PagedResult([], 0, q.Limit, q.Offset); }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "{ articles(filter: { status: { eq: \"published\" }, tags: { name: { eq: \"AI\" } } }) { total } }");
        ParseData(result);

        var logical = captured!.Filter.Should().BeOfType<LogicalFilter>().Subject;
        logical.Children.OfType<ComparisonFilter>().Select(c => c.FieldPath)
            .Should().Contain(new[] { "status", "tags.name" });
    }
```

Add to `tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs` (characterization — the translator itself is unchanged; this proves it is relation-kind-agnostic when the delegate resolves a to-many key). Add a stub and test near the existing `Rel` stub:

```csharp
    // A delegate that resolves to-many keys too (mirrors the relaxed RelationTargets): "tags" is a
    // relation of "article" whose target is "tag"; "articles" is a relation of "category" whose
    // target is "article". The translator does not care about the relation KIND — only that the
    // delegate returns a target for the key.
    private static readonly Func<string, string, string?> RelToMany =
        (coll, key) => (coll, key) switch
        {
            ("article", "tags") => "tag",
            ("category", "articles") => "article",
            _ => null,
        };

    [Fact]
    public void To_many_relation_key_becomes_dotted_comparison()
    {
        var f = FilterInputTranslator.Translate(new Dictionary<string, object?>
        {
            ["tags"] = new Dictionary<string, object?>
            {
                ["name"] = new Dictionary<string, object?> { ["eq"] = "AI" }
            }
        }, "article", RelToMany);

        var cmp = f.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("tags.name");
        cmp.Value.Should().Be("AI");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~GraphQlExecutionTests|FullyQualifiedName~FilterInputTranslatorTests"`
Expected:
- The 4 new `GraphQlExecutionTests` FAIL: without the delegate change, `RelationTargets` returns null for `tags`/`articles`, so `FilterInputTranslator` treats the nested dict as an operator bag (`name` is not an operator token → nothing emitted), and `captured.Filter` is null / not the expected dotted `ComparisonFilter`.
- `To_many_relation_key_becomes_dotted_comparison` **PASSES already** (it uses its own `RelToMany` stub, not the resolver) — it is characterization confirming the translator needs no change.
- All existing tests still PASS.

> If `To_many_relation_key_becomes_dotted_comparison` fails, the translator is NOT kind-agnostic and the spec's core assumption is wrong — stop and re-examine before proceeding.

- [ ] **Step 3: Implement Change 2 in `RelationTargets`**

In `src/Struo.Api/GraphQl/CollectionResolvers.cs`, replace the `RelationTargets` delegate body:

```csharp
    /// <summary>
    /// Delegate for FilterInputTranslator: returns the target collection name when <paramref name="key"/>
    /// is a filterable relation of <paramref name="coll"/> (so a nested filter descends into a dotted
    /// path), else null. M2O participates only with a foreign key (its engine hop dereferences it);
    /// O2M and M2M participate unconditionally (they resolve via the reverse FK / junction) — to-many
    /// paths carry ANY/EXISTS semantics.
    /// </summary>
    private static Func<string, string, string?> RelationTargets(IMetadataProvider metadata) =>
        (coll, key) => metadata.GetCollection(coll)?.Relations
            .FirstOrDefault(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase)
                && ((r.Kind == RelationKind.ManyToOne && r.ForeignKey is not null)
                    || r.Kind is RelationKind.OneToMany or RelationKind.ManyToMany))
            ?.TargetCollection;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~GraphQlExecutionTests|FullyQualifiedName~FilterInputTranslatorTests"`
Expected: all PASS (the 4 new exec tests now capture the dotted paths `tags.name` / `articles.status` / `articles.category.name`; the ANDed test yields `status` + `tags.name`).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/GraphQl/CollectionResolvers.cs tests/Struo.Tests/GraphQl/GraphQlExecutionTests.cs tests/Struo.Tests/GraphQl/FilterInputTranslatorTests.cs
git commit -m "feat(graphql): O2M/M2M relation keys flow to dotted cross-relation filter (8c.2)"
```

---

## Task 3: Engine de-risk — restore the M2M cross-relation filter REST integration test

Rationale: `CrossRelationFilterTests.cs` proves the O2M engine hop end-to-end on SQLite
(`Filter_o2m_category_by_article_id`), but its note records that the M2M test was **deleted in Phase 5.5**
(when Tag was removed) and never restored after Tag was re-added. The M2M `HopAsync` branch therefore has
no current engine-level coverage. Restore it so the "engine already works for to-many" claim is proven for
M2M **before** the live gate — through the same real API + real `RelationFilterResolver` + SQLite path the
GraphQL wiring will use.

**Files:**
- Test: `tests/Struo.Tests/Query/CrossRelationFilterTests.cs` (add one `[Fact]`; test-only, no source change)

**Interfaces:**
- Consumes: the `ApiFactory` integration harness (`[Collection("ApiIntegration")]`), the existing `Post`/`Eq`/`Root` helpers in the file, and the REST endpoints `POST /api/items/{collection}` (create) + `POST /api/items/article/query` (filter). M2M is created by passing the relation name `tags` as an array of target ids on article create (mirrors the Phase 7d sample `Article↔Tag` M2M).

- [ ] **Step 1: Write the failing (or immediately-passing) test**

Add to `tests/Struo.Tests/Query/CrossRelationFilterTests.cs`:

```csharp
    [Fact]
    public async Task Filter_m2m_articles_by_tag_name()
    {
        // Restores the Phase-5.5-deleted M2M cross-relation filter coverage now that Tag exists again.
        // Proves RelationFilterResolver's M2M hop (junction targetFk -> parentFk) end-to-end on SQLite:
        // "articles that have AT LEAST ONE tag named X" (ANY/EXISTS).
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
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~CrossRelationFilterTests"`
Expected: PASS. The M2M engine hop already exists (`RelationFilterResolver.HopAsync` M2M branch), so this should be green immediately, confirming the wiring claim.

> If it FAILS, a latent M2M engine bug exists (SQLite-green ≠ Postgres-correct discipline caught it early). Debug via superpowers:systematic-debugging; a fix would land in `Struo.Infrastructure/Query/RelationFilterResolver.cs` (documented as a de-risk fix, mirroring prior-phase live-gate fixes) — the two Api-only changes stay as-is.

- [ ] **Step 3: Commit**

```bash
git add tests/Struo.Tests/Query/CrossRelationFilterTests.cs
git commit -m "test(query): restore M2M cross-relation filter engine coverage (8c.2 de-risk)"
```

---

## Task 4: Full suite green + docs

**Files:**
- Modify: `docs/ROADMAP.md`

- [ ] **Step 1: Build with warnings-as-errors**

Run: `dotnet build -warnaserror`
Expected: 0 warnings, build succeeds.

- [ ] **Step 2: Run the full backend suite**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj`
Expected: all green. Count = 526 baseline + 7 new (2 schema + 4 exec + 1 translator characterization + 1 M2M REST = actually 8; confirm the printed total is 526 + the number of new `[Fact]`s added, i.e. **534**). Record the exact number for the ROADMAP row.

- [ ] **Step 3: Add the 8c.2 status row to `docs/ROADMAP.md`**

In the bullet list (after the Phase 8c.1 bullet) add a paragraph describing 8c.2 done, following the house style of the 8c.1 bullet. Include: scope (O2M/M2M cross-relation filter, ANY/EXISTS, multi-hop mixed-kind), the two Api-only changes, "Domain/App/Infra untouched, no new packages, no sample change", the restored M2M engine test, the new backend test count, and a placeholder for the live-gate result to be filled after the gate runs. Also update the Phases table row 8c.2 status from `⬜ planned` to the done marker with the spec/plan links:

```
| 8c.2 | GraphQL to-many cross-relation filter (O2M/M2M nested `{Target}FilterInput`, ANY/EXISTS, multi-hop mixed-kind; relaxes the 8c.1 M2O-only guard in `BuildFilterInput` + `RelationTargets`; engine/validator reused) — *second 8c slice* | ✅ done (live-verified: real PG — M2M/O2M/multi-hop/CJK/discrimination/BAD_USER_INPUT) | [spec](superpowers/specs/2026-07-09-phase8c2-graphql-tomany-relation-filter-design.md) | [plan](superpowers/plans/2026-07-09-phase8c2-graphql-tomany-relation-filter.md) |
```

Update the "Next up" bullet: 8c.2 done; remaining 8c work is **8c.3** (multi-level depth>1 nesting/expansion + nested-list `filter/sort/limit/offset` arguments — engine work across Domain/App/Infra) or **Phase 9**.

Update the Phase 8c.2 out-of-scope note in the deferred row (8c.2 in the table currently lists all three features) to reflect that to-many filter shipped in 8c.2 and nesting/nested-list-args moved to 8c.3.

- [ ] **Step 4: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs(roadmap): Phase 8c.2 done — GraphQL to-many cross-relation filter"
```

---

## Live gate (post-implementation verification, real PostgreSQL)

Run after all tasks are green. This is the acceptance authority (SQLite-green ≠ Postgres-correct). Use the
dev API against the live Postgres DB (`web-struo-cms-db`) + Redis, GraphQL endpoint `/graphql`, authenticated
as the bootstrap super-admin. Send non-ASCII payloads as UTF-8 (PowerShell `Invoke-RestMethod` / a UTF-8
body file — the Git Bash console here is Big5; see the live-verify-utf8 memory). Record each query + response.

1. Seed: a category with children + articles; articles with tags (incl. a CJK tag name and a CJK category name).
2. **M2M**: `{ articles(filter: { tags: { name: { eq: "<tag>" } } }) { total items { id } } }` returns only tagged articles.
3. **O2M**: `{ categories(filter: { articles: { status: { eq: "published" } } }) { total } }` returns only categories with a published article.
4. **O2M self-ref**: `{ categories(filter: { children: { name: { eq: "<child>" } } }) { total } }` (uses the real sample `Category.Children`).
5. **Multi-hop mixed kind**: `{ categories(filter: { articles: { category: { name: { eq: "<root>" } } } }) { total } }`.
6. **CJK**: a tag/category name filter round-trips code-point-exact (verify by code point).
7. **Discrimination**: swap the filter value to one that matches nothing → `total` == 0 (proves it filters, not a no-op).
8. **Negative**: `{ articles(filter: { nosuchrel: { name: { eq: "x" } } }) { total } }` → `extensions.code == "BAD_USER_INPUT"`.
9. **Empty match**: `{ articles(filter: { tags: { name: { eq: "does-not-exist" } } }) { total } }` → empty list, not an error.

If any live-gate check fails, debug (superpowers:systematic-debugging); an engine fix lands in
`Struo.Infrastructure` (out of nominal Api-only scope, tracked as a live-gate fix like prior phases). Then
fill the ROADMAP 8c.2 live-gate result and re-commit.

---

## Self-review notes (checked against the spec)

- **Spec coverage:** §5 Change 1 → Task 1; §6 Change 2 → Task 2; §2 ANY/EXISTS semantics → asserted via O2M/M2M spy tests (Task 2) + restored M2M engine test (Task 3); §6.1 nested and/or depth guard → inherited (`QueryValidator`), not re-implemented (documented, no task needed — existing guard); §7 error handling → live-gate negative (unknown relation) + inherited filter; §9 testing (unit translator, schema, integration, live gate) → Tasks 1/2/3 + Live gate section; §10 acceptance (build/test/frontend/live) → Task 4 + Live gate. **Correction vs spec §9:** the spec called the GraphQL-layer integration tests "through the real resolver + ItemService"; the established pattern (and this plan) uses **spy** `FakeGraphQlDataSource` tests to prove the dotted path arrives, and puts real-engine to-many resolution in the **REST** integration test (Task 3, `ApiFactory`/SQLite) + the live gate — same coverage, correct harness.
- **Placeholder scan:** none — every step has concrete code/commands. The ROADMAP row's live-gate result is intentionally filled after the gate (that is data, not a code placeholder).
- **Type consistency:** `RelationKind.ManyToOne/OneToMany/ManyToMany`, `RelationMetadata.{Kind,Name,TargetCollection,ForeignKey}`, `SchemaTypeMapper.TypeName`, `FilterInputTranslator.Translate(filter, collection, relationTarget)`, `ComparisonFilter.{FieldPath,Op,Value}`, `QueryModel.Filter`, `FakeGraphQlDataSource.OnQuery`, `PagedResult(items,total,limit,offset)` — all match the files read during planning.
