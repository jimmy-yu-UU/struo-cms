# Phase 9b — Soft delete Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-collection soft delete (mark-instead-of-remove, with restore and permanent purge) across the REST and GraphQL APIs, enforced by a SqlSugar global query filter so every read path excludes trashed rows by default.

**Architecture:** A collection opts in by having its entity implement a new `ISoftDeletable` interface (mirrors `IAuditable`). A SqlSugar global query filter (`QueryFilter.AddTableFilter<ISoftDeletable>(e => e.DeletedAt == null)`) on the request-scoped client is the "floor": list, get-by-id, deep expansion, cross-relation id-resolution, M2M existence, and inbound-Restrict all exclude trashed rows with no per-path code. Only `QueryAsync`/`GetAsync` accept a `DeletedFilter` mode (Exclude/Only/With) that lifts the floor; restore/purge operate filter-cleared on rows that are by definition trashed. `ItemService.DeleteAsync` branches soft (set `DeletedAt`/`DeletedBy`) vs purge (existing hard delete). GraphQL reaches full parity via a `deleted` list argument, `deleteX(id, purge)`, and a new `restoreX(id)`.

**Tech Stack:** .NET 10 / C# latest, SqlSugarCore, PostgreSQL (runtime) + SQLite (tests), ASP.NET Core Controllers, HotChocolate v16 (GraphQL), xUnit.

## Global Constraints

- All DB access via SqlSugar ORM; zero vendor SQL (§17.4). The global query filter is idiomatic SqlSugar.
- Domain stays free of external packages; persistence attributes live on entities only (§2). `ISoftDeletable` carries no SqlSugar attributes.
- Framework code never references `samples/*` (§2). Only the sample assembly opts its entities in.
- Metadata scanned at startup and cached; no per-request reflection (§17.6).
- Outbound JSON = camelCase. Response envelope stays `{ data }` / `{ error: { message } }` — unifying it is slice 9a, out of scope here.
- Package versions never inferred from memory; no new NuGet packages are needed for this slice.
- Acceptance: `dotnet build -warnaserror` → 0 warnings; `dotnet test` all green (573 baseline + new); frontend untouched (`pnpm test` stays 237); **live gate on real Postgres** before done.
- Spec: `docs/superpowers/specs/2026-07-13-phase9b-soft-delete-design.md`.

---

## File Structure

**Domain (`src/Struo.Domain`)**
- Create `Auditing/ISoftDeletable.cs` — the opt-in marker interface (`DeletedAt`, `DeletedBy`).
- Create `Query/DeletedFilter.cs` — `enum DeletedFilter { Exclude, Only, With }`.

**Domain metadata (`src/Struo.Domain`)**
- Modify `Metadata/Models/CollectionMetadata.cs` — add `bool SoftDelete { get; init; }`.

**Infrastructure (`src/Struo.Infrastructure`)**
- Modify `Metadata/MetadataScanner.cs` — derive `SoftDelete` from `ISoftDeletable`.
- Modify `Persistence/SqlSugarClientFactory.cs` — register the global query filter.
- Modify `Query/SqlSugarItemRepository.cs` — `DeletedFilter` mode on reads; `SoftDeleteAsync`/`RestoreAsync`.

**Application (`src/Struo.Application`)**
- Modify `Query/IItemRepository.cs` — read-mode params + soft-delete/restore signatures.
- Modify `Query/ItemService.cs` — delete soft/purge branch, `RestoreAsync`, read-mode threading.

**Api (`src/Struo.Api`)**
- Modify `Controllers/ItemsController.cs` — `?deleted`, `?purge`, `POST .../{id}/restore`.
- Modify `GraphQl/GraphQlDataSource.cs` — interface + adapter: purge param, `RestoreAsync`, read-mode.
- Modify `GraphQl/CollectionSchemaBuilder.cs` — `deleted` arg on list query fields.
- Modify `GraphQl/CollectionResolvers.cs` — read the `deleted` arg + gate; thread mode.
- Modify `GraphQl/MutationResolvers.cs` — `deleteX(purge)`, add `restoreX`.
- Modify `GraphQl/StruoTypeModule.cs` — register `DeletedFilter` enum + `restoreX` fields.
- Modify `GraphQl/SchemaTypeMapper.cs` — add `RestoreFieldName` helper.

**Sample (`samples/Struo.Sample.Blog`)**
- Modify `Article.cs`, `Category.cs` — implement `ISoftDeletable`.

**Migrations (`db/migrations`)**
- Create `005-soft-delete-columns.sql`.

**Tests (`tests/Struo.Tests`)**
- Create `Query/SoftDeleteTests.cs`, `Query/SoftDeleteRepositoryTests.cs`, `Api/SoftDeleteEndpointTests.cs`, `GraphQl/GraphQlSoftDeleteTests.cs`; extend `Metadata/MetadataScannerTests.cs`.

---

### Task 1: Domain marker interface + delete-filter enum

**Files:**
- Create: `src/Struo.Domain/Auditing/ISoftDeletable.cs`
- Create: `src/Struo.Domain/Query/DeletedFilter.cs`
- Test: `tests/Struo.Tests/Query/SoftDeleteTests.cs`

**Interfaces:**
- Produces: `interface ISoftDeletable { DateTime? DeletedAt { get; set; } Guid? DeletedBy { get; set; } }`; `enum DeletedFilter { Exclude, Only, With }`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Query/SoftDeleteTests.cs
using Struo.Domain.Auditing;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public sealed class SoftDeleteTests
{
    private sealed class Sample : ISoftDeletable
    {
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedBy { get; set; }
    }

    [Fact]
    public void ISoftDeletable_carries_deletion_marker()
    {
        var s = new Sample { DeletedAt = new DateTime(2026, 7, 14), DeletedBy = Guid.Empty };
        Assert.NotNull(s.DeletedAt);
        Assert.Equal(Guid.Empty, s.DeletedBy);
    }

    [Fact]
    public void DeletedFilter_has_three_modes()
    {
        Assert.Equal(0, (int)DeletedFilter.Exclude);
        Assert.Equal(3, System.Enum.GetValues<DeletedFilter>().Length);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SoftDeleteTests"`
Expected: FAIL — `ISoftDeletable`/`DeletedFilter` do not exist (compile error).

- [ ] **Step 3: Write the interface and enum**

```csharp
// src/Struo.Domain/Auditing/ISoftDeletable.cs
namespace Struo.Domain.Auditing;

/// <summary>
/// Opt-in marker for soft-deletable collections (Phase 9b). An entity that implements this
/// interface is soft-deleted (its <see cref="DeletedAt"/> is stamped) instead of being removed;
/// reads exclude it by default. Mirrors <see cref="IAuditable"/>: package-free, no SqlSugar
/// attributes (§2). A null <see cref="DeletedAt"/> means the row is live. The framework assigns
/// <see cref="DeletedAt"/>/<see cref="DeletedBy"/> at delete time.
/// </summary>
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}
```

```csharp
// src/Struo.Domain/Query/DeletedFilter.cs
namespace Struo.Domain.Query;

/// <summary>How a read treats soft-deleted rows (Phase 9b). Default is <see cref="Exclude"/>.</summary>
public enum DeletedFilter { Exclude, Only, With }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SoftDeleteTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Domain/Auditing/ISoftDeletable.cs src/Struo.Domain/Query/DeletedFilter.cs tests/Struo.Tests/Query/SoftDeleteTests.cs
git commit -m "feat(domain): ISoftDeletable marker + DeletedFilter enum (9b)"
```

---

### Task 2: Metadata — derive `SoftDelete` from the interface

**Files:**
- Modify: `src/Struo.Domain/Metadata/Models/CollectionMetadata.cs`
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs:143-198` (`BuildCollection`)
- Test: `tests/Struo.Tests/Metadata/MetadataScannerTests.cs`

**Interfaces:**
- Consumes: `ISoftDeletable` (Task 1).
- Produces: `CollectionMetadata.SoftDelete` (bool); scanner sets it from `type.IsAssignableTo(typeof(ISoftDeletable))`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Metadata/MetadataScannerTests.cs — add
[Fact]
public void Scan_marks_soft_deletable_collection()
{
    var metas = MetadataScanner.ScanTypes([typeof(SoftColl), typeof(HardColl)]);
    Assert.True(metas.Single(m => m.Name == "softColl").SoftDelete);
    Assert.False(metas.Single(m => m.Name == "hardColl").SoftDelete);
}

[CmsCollection("SoftColl")]
private sealed class SoftColl : Struo.Domain.Auditing.ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

[CmsCollection("HardColl")]
private sealed class HardColl
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
}
```

> If `MetadataScannerTests` lacks the `using`s, add: `using SqlSugar; using Struo.Domain.Metadata.Attributes; using Struo.Domain.Metadata.Enums;` (match the file's existing pattern for its other private fixture types).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Scan_marks_soft_deletable_collection"`
Expected: FAIL — `CollectionMetadata` has no `SoftDelete` member (compile error).

- [ ] **Step 3: Add the property**

```csharp
// src/Struo.Domain/Metadata/Models/CollectionMetadata.cs — add inside the record, after AdminOnly
/// <summary>
/// When true, the collection's entity implements <see cref="Struo.Domain.Auditing.ISoftDeletable"/>:
/// DELETE marks the row (DeletedAt set) instead of removing it, reads exclude it by default, and a
/// restore/purge surface applies (Phase 9b). Derived by the scanner from the interface — no attribute.
/// </summary>
public bool SoftDelete { get; init; }
```

- [ ] **Step 4: Set it in the scanner**

```csharp
// src/Struo.Infrastructure/Metadata/MetadataScanner.cs — in BuildCollection, in the returned
// new CollectionMetadata { ... } initializer, add this line (next to AdminOnly = attr.AdminOnly,):
SoftDelete = typeof(Struo.Domain.Auditing.ISoftDeletable).IsAssignableFrom(type),
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Scan_marks_soft_deletable_collection"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Domain/Metadata/Models/CollectionMetadata.cs src/Struo.Infrastructure/Metadata/MetadataScanner.cs tests/Struo.Tests/Metadata/MetadataScannerTests.cs
git commit -m "feat(metadata): derive CollectionMetadata.SoftDelete from ISoftDeletable (9b)"
```

---

### Task 3: Sample entities opt in

**Files:**
- Modify: `samples/Struo.Sample.Blog/Article.cs:13`, `samples/Struo.Sample.Blog/Category.cs:10`
- Test: `tests/Struo.Tests/Metadata/MetadataScannerTests.cs`

**Interfaces:**
- Consumes: `ISoftDeletable` (Task 1), `CollectionMetadata.SoftDelete` (Task 2).
- Produces: `article` and `category` collections are soft-deletable (used by downstream integration tests; SQLite `InitTables` auto-creates the `DeletedAt`/`DeletedBy` columns from the new properties).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Metadata/MetadataScannerTests.cs — add
[Fact]
public void Sample_article_and_category_are_soft_deletable()
{
    var metas = MetadataScanner.Scan(typeof(Struo.Sample.Blog.Article).Assembly);
    Assert.True(metas.Single(m => m.Name == "article").SoftDelete);
    Assert.True(metas.Single(m => m.Name == "category").SoftDelete);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Sample_article_and_category_are_soft_deletable"`
Expected: FAIL — assertions false (entities don't implement the interface yet).

- [ ] **Step 3: Implement the interface on both entities**

```csharp
// samples/Struo.Sample.Blog/Article.cs — change the class declaration:
public sealed class Article : AuditableEntity, ISoftDeletable
```
Add these two properties (place them just after the `Id` property, both nullable so SqlSugar maps them to NULL columns via the existing `SqlSugarClientFactory` nullable-value-type hook):
```csharp
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
```

```csharp
// samples/Struo.Sample.Blog/Category.cs — change the class declaration:
public sealed class Category : AuditableEntity, ISoftDeletable
```
Add the same two properties after `Id`:
```csharp
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
```

> `Struo.Domain.Auditing` is already imported in both files (`using Struo.Domain.Auditing;`). `DeletedAt`/`DeletedBy` carry no `[CmsField]`, so they are NOT projected/writable (they are server-managed, like the audit fields but without the audit-convention auto-inclusion). No `[SugarColumn]` needed — the nullable-value-type hook maps them to nullable columns.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Sample_article_and_category_are_soft_deletable"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add samples/Struo.Sample.Blog/Article.cs samples/Struo.Sample.Blog/Category.cs tests/Struo.Tests/Metadata/MetadataScannerTests.cs
git commit -m "feat(sample): Article + Category opt in to soft delete (9b)"
```

---

### Task 4: Infrastructure — register the SqlSugar global query filter

**Files:**
- Modify: `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs:117-120`
- Test: `tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs`

**Interfaces:**
- Consumes: `ISoftDeletable` (Task 1); soft-deletable `Article` (Task 3).
- Produces: every `Queryable` over an `ISoftDeletable` entity excludes rows with non-null `DeletedAt` **by default**. The floor for all read paths.

- [ ] **Step 1: Write the failing test**

This test uses the existing SQLite test-database + repository harness. Mirror the setup in `tests/Struo.Tests/Query/SqlSugarItemRepositoryTests.cs` (same fixtures: `SqliteTestDatabase`, `InitTables`, seed via the client). The assertion: a soft-deleted `article` row is absent from a default query.

```csharp
// tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs
// Build the client via SqlSugarClientFactory.Create (so the global filter is registered), init the
// sample tables, insert two articles, stamp DeletedAt on one, then QueryAsync and assert only the
// live row returns. (Follow SqlSugarItemRepositoryTests for the exact fixture wiring in this repo.)
[Fact]
public async Task Default_query_excludes_soft_deleted_rows()
{
    using var h = SoftDeleteRepositoryHarness.Create();      // helper mirroring SqlSugarItemRepositoryTests setup
    var live = await h.InsertArticleAsync(status: "published");
    var trashed = await h.InsertArticleAsync(status: "published");
    await h.SoftDeleteRawAsync(trashed);                     // sets DeletedAt = now directly via the client

    var result = await h.Repository.QueryAsync("article",
        new QueryModel(null, null, [], 100, 0, null), [], null, default);

    var ids = result.Rows.Select(h.IdOf).ToList();
    Assert.Contains(live, ids);
    Assert.DoesNotContain(trashed, ids);
}
```

> Implement `SoftDeleteRepositoryHarness` in the test file by copying the client/repository construction from `SqlSugarItemRepositoryTests` (it already builds an `ISqlSugarClient` + `SqlSugarItemRepository` over the sample types). `SoftDeleteRawAsync` runs `client.Updateable<Article>().SetColumns(a => a.DeletedAt == DateTime.UtcNow).Where(a => a.Id == id).ExecuteCommandAsync()`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SoftDeleteRepositoryTests.Default_query_excludes_soft_deleted_rows"`
Expected: FAIL — trashed row still returned (no filter registered yet).

- [ ] **Step 3: Register the global filter**

```csharp
// src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs
// After `var client = new SqlSugarClient(config);` and before AuditAop.Register, add:

        // Phase 9b: soft-delete floor. Every Queryable over an ISoftDeletable entity excludes rows
        // whose DeletedAt is set. Applies to list/get/deep-expansion/cross-relation id-resolution/
        // M2M existence/inbound-Restrict with no per-path code. Reads that need trashed rows
        // (?deleted=only|with, restore, purge) clear this filter per-query (see the repository).
        client.QueryFilter.AddTableFilter<ISoftDeletable>(e => e.DeletedAt == null);
```
Add `using Struo.Domain.Auditing;` to the file's usings.

> **Verify the SqlSugarCore API surface** for this version: the method is `client.QueryFilter.AddTableFilter<T>(Expression<Func<T,bool>>)`. If interface-typed table filters are not honored for the reflection-built `db.Queryable(entityType)` path in this SqlSugarCore version, fall back to registering the filter per concrete soft-deletable entity type in DI startup (enumerate `IEntityTypeCollector` types implementing `ISoftDeletable` and call `AddTableFilter<TConcrete>`), keeping the same lambda. The test above is the gate either way.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SoftDeleteRepositoryTests.Default_query_excludes_soft_deleted_rows"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs
git commit -m "feat(infra): SqlSugar global query filter excludes soft-deleted rows (9b)"
```

---

### Task 5: Repository — read modes + soft-delete/restore operations

**Files:**
- Modify: `src/Struo.Application/Query/IItemRepository.cs:14-18`
- Modify: `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` (`QueryAsync`, `GetByIdAsync`, new `SoftDeleteAsync`/`RestoreAsync`)
- Test: `tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs`

**Interfaces:**
- Consumes: `DeletedFilter` (Task 1); the global filter (Task 4).
- Produces:
  - `QueryAsync(..., DeletedFilter mode = DeletedFilter.Exclude, CancellationToken ct = default)`
  - `GetByIdAsync(string collection, string id, DeletedFilter mode = DeletedFilter.Exclude, CancellationToken ct = default)`
  - `Task<bool> SoftDeleteAsync(string collection, string id, DateTime deletedAt, Guid? deletedBy, CancellationToken ct = default)`
  - `Task<bool> RestoreAsync(string collection, string id, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs — add
[Fact]
public async Task Query_with_Only_returns_just_trashed()
{
    using var h = SoftDeleteRepositoryHarness.Create();
    var live = await h.InsertArticleAsync(status: "published");
    var trashed = await h.InsertArticleAsync(status: "published");
    await h.Repository.SoftDeleteAsync("article", trashed.ToString(), DateTime.UtcNow, null, default);

    var only = await h.Repository.QueryAsync("article",
        new QueryModel(null, null, [], 100, 0, null), [], null, DeletedFilter.Only, default);
    var ids = only.Rows.Select(h.IdOf).ToList();
    Assert.Contains(trashed, ids);
    Assert.DoesNotContain(live, ids);
}

[Fact]
public async Task Restore_makes_row_visible_again()
{
    using var h = SoftDeleteRepositoryHarness.Create();
    var id = await h.InsertArticleAsync(status: "published");
    await h.Repository.SoftDeleteAsync("article", id.ToString(), DateTime.UtcNow, null, default);
    Assert.Null(await h.Repository.GetByIdAsync("article", id.ToString(), DeletedFilter.Exclude, default));

    var restored = await h.Repository.RestoreAsync("article", id.ToString(), default);
    Assert.True(restored);
    Assert.NotNull(await h.Repository.GetByIdAsync("article", id.ToString(), DeletedFilter.Exclude, default));
}

[Fact]
public async Task GetById_with_With_finds_trashed_row()
{
    using var h = SoftDeleteRepositoryHarness.Create();
    var id = await h.InsertArticleAsync(status: "published");
    await h.Repository.SoftDeleteAsync("article", id.ToString(), DateTime.UtcNow, null, default);
    Assert.NotNull(await h.Repository.GetByIdAsync("article", id.ToString(), DeletedFilter.With, default));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SoftDeleteRepositoryTests"`
Expected: FAIL — new overloads/methods don't exist (compile error).

- [ ] **Step 3: Extend the repository interface**

```csharp
// src/Struo.Application/Query/IItemRepository.cs — replace the QueryAsync + GetByIdAsync lines:
Task<QueryResult> QueryAsync(string collection, QueryModel query, IReadOnlyList<string> searchableFields,
    string? queryLocale = null, DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default);
Task<object?> GetByIdAsync(string collection, string id,
    DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default);

// add near DeleteAsync:
/// <summary>Stamps DeletedAt/DeletedBy on the row (soft delete). Returns false if the id is unknown.
/// Operates with the soft-delete filter cleared so an already-trashed row is still found (idempotent).</summary>
Task<bool> SoftDeleteAsync(string collection, string id, DateTime deletedAt, Guid? deletedBy, CancellationToken ct = default);
/// <summary>Clears DeletedAt/DeletedBy (restore). Returns false if the id is unknown. Filter-cleared.</summary>
Task<bool> RestoreAsync(string collection, string id, CancellationToken ct = default);
```
Add `using Struo.Domain.Query;` if not already present (it is — `QueryModel`/`FilterNode` come from there).

- [ ] **Step 4: Implement in `SqlSugarItemRepository`**

- Update `QueryAsync`'s signature to accept `DeletedFilter deleted = DeletedFilter.Exclude` (before `ct`), and thread it into the generic query helper. In the generic path that builds the `ISugarQueryable<T>` (`RunQueryAsync<T>` / where `db.Queryable<T>()` is created), apply:
```csharp
// when building the queryable (generic T):
var q = db.Queryable<T>();
if (deleted != DeletedFilter.Exclude && typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
    q = q.ClearFilter<ISoftDeletable>();            // lift the global floor for Only/With
if (deleted == DeletedFilter.Only && typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
    q = q.Where(x => ((ISoftDeletable)(object)x!).DeletedAt != null);
// ... existing conditionals/sort/paging apply to q
```
> If SqlSugar cannot compile the cast-based `Where` predicate, express `Only` as a `ConditionalModel` on the `DeletedAt` column (the repo already builds `List<IConditionalModel>`; add `new ConditionalModel { FieldName = "DeletedAt", ConditionalType = ConditionalType.IsNot, FieldValue = null }`). Either satisfies the Task-5 `Only` test.
- Update `GetByIdAsync` to accept `DeletedFilter deleted = DeletedFilter.Exclude` and, when `With`/`Only`, call `.ClearFilter<ISoftDeletable>()` on the queryable before `.InSingleAsync(id)` (guard on `typeof(ISoftDeletable).IsAssignableFrom(entityType)`).
- Add `SoftDeleteAsync` (generic dispatch, mirroring `DeleteGenericAsync`):
```csharp
private async Task<bool> SoftDeleteGenericAsync<T>(object id, DateTime deletedAt, Guid? deletedBy, CancellationToken ct)
    where T : class, new()
{
    var affected = await db.Updateable<T>()
        .SetColumns(e => ((ISoftDeletable)(object)e!).DeletedAt == deletedAt)
        .SetColumns(e => ((ISoftDeletable)(object)e!).DeletedBy == deletedBy)
        .Where($"{PkColumn<T>()} = @id", new { id })   // reuse the repo's existing PK-where pattern
        .ExecuteCommandAsync();
    return affected > 0;
}
```
> Use the repository's established generic-dispatch + PK-resolution pattern (see `DeleteGenericAsync`/`UpdateGenericAsync`); the exact `SetColumns`/`Where` form should match how those methods target the PK in this codebase. Updateable is not subject to the query filter, so no clear is needed for the update itself; the row is located by id regardless of `DeletedAt`.
- Add `RestoreAsync` the same way, setting `DeletedAt == null` and `DeletedBy == null`.
- Public `SoftDeleteAsync`/`RestoreAsync` dispatch to the generic helpers via the cached-`MethodInfo` pattern used by the other methods (add `SoftDeleteGenericAsyncDef`/`RestoreGenericAsyncDef` alongside `DeleteGenericAsyncDef`).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SoftDeleteRepositoryTests"`
Expected: PASS (all 4 in the file).

- [ ] **Step 6: Run the full repository suite (no regressions)**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SqlSugarItemRepositoryTests"`
Expected: PASS — default-mode callers are unaffected (new params default to Exclude).

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Application/Query/IItemRepository.cs src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs
git commit -m "feat(infra): repository read modes + soft-delete/restore ops (9b)"
```

---

### Task 6: Application — `ItemService` soft/purge/restore + read-mode threading

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs` (`QueryAsync`, `GetAsync`, `DeleteAsync`, new `RestoreAsync`)
- Test: `tests/Struo.Tests/Query/SoftDeleteTests.cs` (unit) + `tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs` (integration via `ItemService`)

**Interfaces:**
- Consumes: repository ops (Task 5); `ICurrentUserAccessor` (existing); `CollectionMetadata.SoftDelete` (Task 2).
- Produces:
  - `QueryAsync(string collection, QueryModel raw, string? locale = null, DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)`
  - `GetAsync(..., DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)`
  - `DeleteAsync(string collection, string id, bool purge = false, CancellationToken ct = default)`
  - `Task<IReadOnlyDictionary<string, object?>?> RestoreAsync(string collection, string id, CancellationToken ct = default)`

> **Note:** `ItemService` currently has no injected current-user accessor. Add `ICurrentUserAccessor currentUser` to its primary-constructor parameter list (from `Struo.Application.Abstractions`) — DI already registers it. Read the actor as `currentUser.UserId` (confirm the exact member name on `ICurrentUserAccessor`; use whatever `AuditAop` reads for `CreatedBy`/`UpdatedBy` so the actor semantics match).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs — add (these go through ItemService end-to-end)
[Fact]
public async Task Delete_soft_deletes_and_stamps_actor()
{
    using var h = SoftDeleteRepositoryHarness.Create(currentUserId: KnownUser);
    var id = await h.InsertArticleAsync(status: "published");

    Assert.True(await h.Service.DeleteAsync("article", id.ToString(), purge: false, default));

    Assert.Null(await h.Service.GetAsync("article", id.ToString(), null, null, DeletedFilter.Exclude, default));
    var trashed = await h.Repository.GetByIdAsync("article", id.ToString(), DeletedFilter.With, default);
    Assert.NotNull(trashed);
    Assert.Equal(KnownUser, ((ISoftDeletable)trashed!).DeletedBy);
}

[Fact]
public async Task Purge_hard_deletes_a_soft_delete_collection()
{
    using var h = SoftDeleteRepositoryHarness.Create();
    var id = await h.InsertArticleAsync(status: "published");
    await h.Service.DeleteAsync("article", id.ToString(), purge: false, default);   // soft
    Assert.True(await h.Service.DeleteAsync("article", id.ToString(), purge: true, default)); // purge
    Assert.Null(await h.Repository.GetByIdAsync("article", id.ToString(), DeletedFilter.With, default));
}

[Fact]
public async Task Restore_clears_marker_via_service()
{
    using var h = SoftDeleteRepositoryHarness.Create();
    var id = await h.InsertArticleAsync(status: "published");
    await h.Service.DeleteAsync("article", id.ToString(), purge: false, default);
    var restored = await h.Service.RestoreAsync("article", id.ToString(), default);
    Assert.NotNull(restored);
    Assert.NotNull(await h.Service.GetAsync("article", id.ToString(), null, null, DeletedFilter.Exclude, default));
}

[Fact]
public async Task Restore_unknown_id_returns_null()
{
    using var h = SoftDeleteRepositoryHarness.Create();
    Assert.Null(await h.Service.RestoreAsync("article", Guid.NewGuid().ToString(), default));
}
```

> Extend `SoftDeleteRepositoryHarness` to expose `Service` (an `ItemService` built with the same client/registry/permissions used by existing `ItemService` integration tests — see `DeepExpansionTests`/`RelationWriteTests` for the wiring; use `AllowAllPermissionService` and `TestCurrentUserAccessor` from `tests/Struo.Tests/Support`). `KnownUser` is a fixed `Guid`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SoftDeleteRepositoryTests"`
Expected: FAIL — `DeleteAsync(purge:)`, `RestoreAsync`, and the `DeletedFilter` overloads don't exist (compile error).

- [ ] **Step 3: Thread the read mode**

```csharp
// ItemService.QueryAsync — add `DeletedFilter deleted = DeletedFilter.Exclude` before ct, and pass it:
var result = await repository.QueryAsync(collection, validated, searchable, queryLocale, deleted, ct);
// ItemService.GetAsync — add `DeletedFilter deleted = DeletedFilter.Exclude` before ct, and pass it:
var entity = await repository.GetByIdAsync(collection, id, deleted, ct);
```

- [ ] **Step 4: Branch `DeleteAsync` soft vs purge**

```csharp
// ItemService.DeleteAsync — change signature to add `bool purge = false` before ct.
// The inbound-Restrict block stays UNCHANGED (it now excludes trashed sources for free via the
// global filter). Replace the final `return await repository.DeleteAsync(collection, id, ct);` with:

        if (meta.SoftDelete && !purge)
        {
            var actor = currentUser.UserId;   // same actor source as CreatedBy/UpdatedBy
            return await repository.SoftDeleteAsync(collection, id, DateTime.UtcNow, actor, ct);
        }
        return await repository.DeleteAsync(collection, id, ct);   // purge, or non-soft collection
```

- [ ] **Step 5: Add `RestoreAsync`**

```csharp
// ItemService — add:
public async Task<IReadOnlyDictionary<string, object?>?> RestoreAsync(
    string collection, string id, CancellationToken ct = default)
{
    var meta = Meta(collection);
    if (!permissions.CanDelete(collection)) throw new PermissionDeniedException("Delete not permitted.");
    RequireSuperAdminForAdminOnly(meta);

    // Find the row ignoring the soft-delete floor (it is, by definition, trashed).
    var entity = await repository.GetByIdAsync(collection, id, DeletedFilter.With, ct);
    if (entity is null) return null;                       // unknown id -> 404

    if (entity is ISoftDeletable sd && sd.DeletedAt is not null)
        await repository.RestoreAsync(collection, id, ct);  // no-op idempotent if already live

    var restored = await repository.GetByIdAsync(collection, id, DeletedFilter.Exclude, ct);
    return restored is null ? null : Project(restored, meta, null);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SoftDeleteRepositoryTests"`
Expected: PASS.

- [ ] **Step 7: Fix compile fallout + full build**

The `GraphQlDataSource` adapter and any other `ItemService.DeleteAsync` callers now need the new optional param — optional defaults keep them compiling, but confirm:

Run: `dotnet build -warnaserror`
Expected: 0 warnings, build succeeds. (If `ItemServiceGraphQlDataSource.DeleteAsync` fails to compile, it is fixed in Task 8.)

- [ ] **Step 8: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs
git commit -m "feat(app): ItemService soft-delete/purge/restore + read-mode threading (9b)"
```

---

### Task 7: REST — `?deleted`, `?purge`, restore endpoint

**Files:**
- Modify: `src/Struo.Api/Controllers/ItemsController.cs`
- Test: `tests/Struo.Tests/Api/SoftDeleteEndpointTests.cs`

**Interfaces:**
- Consumes: `ItemService` read-mode/soft/purge/restore (Task 6); `ICurrentPermissions.CanDelete` (existing, via the permission service the controller already uses indirectly through `ItemService`).
- Produces: REST surface — `GET ...?deleted=`, `DELETE ...?purge=`, `POST .../{id}/restore`.

> **Permission gating for `?deleted=only|with`:** `ItemService.QueryAsync` does not itself check `CanDelete` (it checks `CanRead`). Gate in the controller: when `deleted != Exclude`, require `permissions.CanDelete(collection)` → else throw `PermissionDeniedException` (mapped to 403/401 by the Program.cs middleware). Inject `ICurrentPermissions`/`IPermissionService` into the controller (whichever the RBAC layer exposes for a synchronous check — see how `UsersController`/`FilesController` read permissions).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Struo.Tests/Api/SoftDeleteEndpointTests.cs
// Use the existing ApiFactory integration harness (see ItemsEndpointTests for auth + JSON helpers).
[Fact]
public async Task Delete_soft_deletes_then_list_excludes_then_only_shows_then_restore_reappears()
{
    // create an article (with default-locale translation) -> 201
    // DELETE /api/items/article/{id} -> 204
    // GET  /api/items/article -> row absent
    // GET  /api/items/article?deleted=only -> row present
    // POST /api/items/article/{id}/restore -> 200 { data }
    // GET  /api/items/article -> row present again
}

[Fact]
public async Task Purge_removes_permanently()
{
    // DELETE /api/items/article/{id}         -> 204 (soft)
    // DELETE /api/items/article/{id}?purge=true -> 204 (purge)
    // GET  /api/items/article/{id}?deleted=with -> 404
}

[Fact]
public async Task Deleted_only_requires_delete_permission()
{
    // as a user WITHOUT delete permission on article:
    // GET /api/items/article?deleted=only -> 403 (or 401 if anonymous)
}

[Fact]
public async Task Unknown_deleted_value_is_400()
{
    // GET /api/items/article?deleted=banana -> 400
}
```

> Fill in the bodies following `ItemsEndpointTests`' patterns for authenticated requests, article creation with `translations`, and status-code assertions. Use the bootstrap super-admin for the happy paths and a delete-less role for the 403 test (see `RbacEnforcementTests` for constructing a scoped-permission user).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SoftDeleteEndpointTests"`
Expected: FAIL — endpoints/params don't exist (404 on restore, `deleted`/`purge` ignored).

- [ ] **Step 3: Implement in `ItemsController`**

```csharp
// Parse the deleted mode (List + Query methods). Add a helper:
private DeletedFilter DeletedMode()
{
    if (!Request.Query.TryGetValue("deleted", out var dv) || string.IsNullOrWhiteSpace(dv))
        return DeletedFilter.Exclude;
    return dv.ToString().ToLowerInvariant() switch
    {
        "exclude" => DeletedFilter.Exclude,
        "only" => DeletedFilter.Only,
        "with" => DeletedFilter.With,
        _ => throw new Struo.Domain.Query.QueryException("Query parameter 'deleted' must be exclude|only|with.")
    };
}

// In List (and Query): compute mode, gate, pass through:
var mode = DeletedMode();
if (mode != DeletedFilter.Exclude && !permissions.CanDelete(collection))
    throw new Struo.Domain.Query.PermissionDeniedException("Viewing deleted items requires delete permission.");
var result = await items.QueryAsync(collection, raw, Locale(), mode, ct);

// In Get(id): same mode + gate, then:
var item = await items.GetAsync(collection, id, deep, Locale(), mode, ct);

// Delete: add ?purge
[HttpDelete("{id}")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public async Task<IActionResult> Delete(string collection, string id, CancellationToken ct)
{
    var purge = Request.Query.TryGetValue("purge", out var pv)
                && string.Equals(pv.ToString(), "true", StringComparison.OrdinalIgnoreCase);
    var ok = await items.DeleteAsync(collection, id, purge, ct);
    return ok ? NoContent() : NotFound();
}

// Restore:
[HttpPost("{id}/restore")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public async Task<IActionResult> Restore(string collection, string id, CancellationToken ct)
{
    var restored = await items.RestoreAsync(collection, id, ct);
    return restored is null ? NotFound() : Ok(new { data = restored });
}
```
Add the constructor dependency for permissions (e.g. `ItemsController(ItemService items, IPermissionService permissions)`) — match the exact permission-service type/method the codebase exposes for `CanDelete(collection)` (as used in `ItemService`). Add `using Struo.Domain.Query;` for `DeletedFilter`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~SoftDeleteEndpointTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Controllers/ItemsController.cs tests/Struo.Tests/Api/SoftDeleteEndpointTests.cs
git commit -m "feat(api): REST soft-delete/restore/purge + ?deleted mode (9b)"
```

---

### Task 8: GraphQL — full read/write parity

**Files:**
- Modify: `src/Struo.Api/GraphQl/GraphQlDataSource.cs`
- Modify: `src/Struo.Api/GraphQl/SchemaTypeMapper.cs` (add `RestoreFieldName`)
- Modify: `src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs` (add `deleted` arg to list query fields)
- Modify: `src/Struo.Api/GraphQl/CollectionResolvers.cs` (read `deleted` arg + gate + thread mode)
- Modify: `src/Struo.Api/GraphQl/MutationResolvers.cs` (`deleteX(purge)`, add `RestoreField`)
- Modify: `src/Struo.Api/GraphQl/StruoTypeModule.cs` (register `DeletedFilter` enum + `restoreX` fields)
- Test: `tests/Struo.Tests/GraphQl/GraphQlSoftDeleteTests.cs`

**Interfaces:**
- Consumes: `ItemService`/`IGraphQlDataSource` (Task 6), `DeletedFilter` (Task 1), existing `SchemaTypeMapper.DeleteFieldName`/`SelectionDeepSpec` patterns.
- Produces: GraphQL `deleted: DeletedFilter = EXCLUDE` on list queries; `deleteX(id, purge: Boolean = false)`; `restoreX(id): X`.

- [ ] **Step 1: Update the data-source seam (interface + adapter)**

```csharp
// GraphQlDataSource.cs — update the interface signatures + adapter delegations:
Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, DeletedFilter deleted, CancellationToken ct);
Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct);
Task<bool> DeleteAsync(string collection, string id, bool purge, CancellationToken ct);
Task<IReadOnlyDictionary<string, object?>?> RestoreAsync(string collection, string id, CancellationToken ct);
// Create/Update unchanged.

// ItemServiceGraphQlDataSource — implement accordingly:
public Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, DeletedFilter deleted, CancellationToken ct)
    => items.QueryAsync(collection, query, locale, deleted, ct);
public Task<bool> DeleteAsync(string collection, string id, bool purge, CancellationToken ct)
    => items.DeleteAsync(collection, id, purge, ct);
public Task<IReadOnlyDictionary<string, object?>?> RestoreAsync(string collection, string id, CancellationToken ct)
    => items.RestoreAsync(collection, id, ct);
```
Add `using Struo.Domain.Query;` if needed. Update `tests/Struo.Tests/GraphQl/FakeGraphQlDataSource.cs` to match the new signatures (add `purge`/`deleted` params + a `RestoreAsync`).

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Struo.Tests/GraphQl/GraphQlSoftDeleteTests.cs — use the GraphQlEndpointTests harness pattern
[Fact]
public async Task Query_excludes_deleted_and_ONLY_shows_them() { /* create, deleteX, query default absent, deleted: ONLY present */ }

[Fact]
public async Task DeleteX_soft_deletes_and_restoreX_reverts() { /* deleteX -> true; query absent; restoreX -> node; query present */ }

[Fact]
public async Task DeleteX_purge_removes_permanently() { /* deleteX(purge:true) -> true; deleted: WITH absent */ }

[Fact]
public async Task Deleted_ONLY_without_delete_permission_is_FORBIDDEN() { /* error code FORBIDDEN */ }
```
> Follow `GraphQlEndpointTests`/`GraphQlMutationExecutionTests` for POSTing GraphQL over the `ApiFactory` and asserting `data`/`errors[].extensions.code`. Drive via `Category` for the non-i18n happy paths (create needs no translations), matching how the mutation tests avoid the default-locale gate; use `Article` where i18n is not central.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlSoftDeleteTests"`
Expected: FAIL — `deleted` arg, `purge` arg, `restoreX` don't exist.

- [ ] **Step 4: Register the `DeletedFilter` enum + list arg**

- In `SchemaTypeMapper.cs` add: `public static string RestoreFieldName(string collection) => "restore" + Pascal(collection);` (mirror `DeleteFieldName`; reuse the file's existing name-casing helper).
- In `CollectionSchemaBuilder.cs`, where the list query field's arguments are added (the same block as `sort`/`limit`/`offset` at lines ~119-121, i.e. the `xs(...)` field, NOT the nested-relation block), add:
```csharp
field.Arguments.Add(new ArgumentConfiguration("deleted", null, TypeReference.Parse("DeletedFilter")));
```
- In `StruoTypeModule.cs`, register an `EnumType` named `DeletedFilter` with values `EXCLUDE`/`ONLY`/`WITH` (mirror how existing enum/shared types are added to the schema; HotChocolate maps the C# `DeletedFilter` enum — bind it so `EXCLUDE`→`Exclude`). Also add, per collection, the `restoreX` mutation field: `mutationType.Fields.Add(MutationResolvers.RestoreField(collection))` next to where `DeleteField` is added.

- [ ] **Step 5: Read the `deleted` arg + gate in `CollectionResolvers`**

In the list-query resolver (where `QueryModel` is assembled and `IGraphQlDataSource.QueryAsync` is called), read the arg and gate:
```csharp
var deleted = ctx.ArgumentValue<DeletedFilter?>("deleted") ?? DeletedFilter.Exclude;
if (deleted != DeletedFilter.Exclude && !ctx.Service<IPermissionService>().CanDelete(collection))
    throw new PermissionDeniedException("Viewing deleted items requires delete permission.");
var result = await ctx.Service<IGraphQlDataSource>().QueryAsync(collection, query, locale, deleted, ctx.RequestAborted);
```
Match the exact permission-service type used elsewhere in the Api layer for `CanDelete`.

- [ ] **Step 6: `deleteX(purge)` + `restoreX` in `MutationResolvers`**

```csharp
// DeleteField — add the purge argument:
config.Arguments.Add(new ArgumentConfiguration("purge", null, TypeReference.Parse("Boolean")));

// ResolveDelete — read it:
var purge = ctx.ArgumentValue<bool?>("purge") ?? false;
return await ctx.Service<IGraphQlDataSource>().DeleteAsync(collection, id, purge, ctx.RequestAborted);

// Add RestoreField + ResolveRestore (mirror UpdateField's re-read pattern):
internal static ObjectFieldConfiguration RestoreField(string collection)
{
    var config = new ObjectFieldConfiguration(
        SchemaTypeMapper.RestoreFieldName(collection), null,
        TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
        resolver: ctx => ResolveRestore(ctx, collection));
    config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
    return config;
}

private static async ValueTask<object?> ResolveRestore(IResolverContext ctx, string collection)
{
    var id = ctx.ArgumentValue<string>("id");
    var restored = await ctx.Service<IGraphQlDataSource>().RestoreAsync(collection, id, ctx.RequestAborted);
    return restored;   // null -> GraphQL null (REST 404 parity); shape already matches a query node
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlSoftDeleteTests"`
Expected: PASS.

- [ ] **Step 8: Full build + suite (no regressions, schema still valid)**

Run: `dotnet build -warnaserror && dotnet test tests/Struo.Tests`
Expected: 0 warnings; all green (573 baseline + all new tests). Also confirm the GraphQL schema-snapshot/smoke tests (`GraphQlEndpointSmokeTests`, `GraphQlMutationSchemaTests`) still pass — update any committed schema snapshot to include the new `deleted` arg / `restoreX` / `DeletedFilter` enum.

- [ ] **Step 9: Commit**

```bash
git add src/Struo.Api/GraphQl/ tests/Struo.Tests/GraphQl/
git commit -m "feat(graphql): soft-delete parity — deleted arg, deleteX(purge), restoreX (9b)"
```

---

### Task 9: Migration script + live gate + roadmap

**Files:**
- Create: `db/migrations/005-soft-delete-columns.sql`
- Modify: `docs/ROADMAP.md` (status + phase table row)

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: Write the migration**

```sql
-- db/migrations/005-soft-delete-columns.sql
-- Phase 9b: add soft-delete columns to opted-in collections (entities implementing ISoftDeletable).
-- InitTables adds tables, not columns, so live DBs provisioned before 9b need this. Idempotent.
ALTER TABLE articles   ADD COLUMN IF NOT EXISTS deleted_at timestamptz NULL;
ALTER TABLE articles   ADD COLUMN IF NOT EXISTS deleted_by uuid        NULL;
ALTER TABLE categories ADD COLUMN IF NOT EXISTS deleted_at timestamptz NULL;
ALTER TABLE categories ADD COLUMN IF NOT EXISTS deleted_by uuid        NULL;
```
> Confirm the physical column names SqlSugar CodeFirst emits for `DeletedAt`/`DeletedBy` on Postgres (snake vs camel) against how the existing audit columns (`created_at`/`CreatedAt`) are named in this DB, and match them. Adjust the DDL to the actual naming before applying.

- [ ] **Step 2: Apply to the live DB + run the live gate (real Postgres)**

Apply `005-soft-delete-columns.sql` to `web-struo-cms-db`, then run the dev API against live PG + Redis and verify (send UTF-8 via PowerShell `Invoke-RestMethod` or a UTF-8 file, per the live-verify note):
- create `article` (en + zh-TW) → soft `DELETE` → default list **excludes** it → `?deleted=only` **shows** it → `POST .../{id}/restore` → default list **includes** it again → `DELETE ...?purge=true` → `GET ...?deleted=with` → **404**.
- CJK title round-trips **code-point-exact** through the soft/restore cycle.
- **inbound-Restrict live-only:** soft-delete a `Category`, confirm a *live* `Article` referencing it still 409s on the Category's purge, while soft-deleting the *Article* first then the Category proceeds (trashed referrer does not block) — exercises the `OnDelete`/Restrict interaction on real PG.
- deep expansion + relation resolution **exclude** trashed rows.
- **GraphQL parity:** `xs(deleted: ONLY)`, `deleteX`, `deleteX(purge:true)`, `restoreX` all behave identically over `/graphql`.
- The Approach-A risk: confirm the global filter composes with the raw ORDER-BY sort subquery + `IN` queries (no SQL error, correct rows) — the decisive check.

- [ ] **Step 3: Update the roadmap**

Add a Phase 9b "done & live-verified" bullet + a `| 9b | Soft delete … | ✅ … |` row (mirror the 8c.3b entries), recording the new `dotnet test` count and the live-gate result.

- [ ] **Step 4: Commit**

```bash
git add db/migrations/005-soft-delete-columns.sql docs/ROADMAP.md
git commit -m "docs(roadmap): Phase 9b soft delete done + live-verified; migration 005 (9b)"
```

---

## Self-Review

**Spec coverage:**
- §2–3 interface-driven opt-in + `DeletedFilter` → Tasks 1–3. ✅
- §2 global-filter (Approach A) → Task 4. ✅
- §4 read modes + soft/restore repo ops → Task 5. ✅
- §5 `ItemService` soft/purge/restore + Restrict-live-only (free via filter) + read-mode → Task 6. ✅
- §6 REST `?deleted`/`?purge`/restore + gating → Task 7. ✅
- §7 GraphQL parity (enum, `deleted` arg, `deleteX(purge)`, `restoreX`) → Task 8. ✅
- §8 error handling (reused exceptions) → implicit across Tasks 6–8 (no new types). ✅
- §9 testing (unit/integration/N+1/live) → Tasks 2,4,5,6,7,8 + Task 9 live gate. ✅
- §10 migration + sample → Tasks 3 + 9. ✅
- §11 acceptance gate → Task 8 Step 8 + Task 9. ✅
- §12 out-of-scope items → not implemented (correct). ✅

**Placeholder scan:** Test-body outlines in Tasks 7–8 intentionally reference the existing harness patterns (`ItemsEndpointTests`, `GraphQlEndpointTests`, `RbacEnforcementTests`) rather than inlining ~200 lines of harness boilerplate the repo already standardizes; every such outline names the exact behaviour to assert and the exact HTTP/GraphQL calls. All production-code steps show complete code. The two "verify SqlSugar API" / "confirm physical column names" notes are genuine environment checks with concrete fallbacks, not deferred work.

**Type consistency:** `DeletedFilter` (Exclude/Only/With) used identically across repository, `ItemService`, controller, GraphQL. `SoftDeleteAsync(collection,id,DateTime,Guid?,ct)` / `RestoreAsync(collection,id,ct)` signatures match between `IItemRepository` (Task 5) and callers (Task 6). `DeleteAsync(...,bool purge=false,ct)` consistent across `ItemService` (Task 6), `IGraphQlDataSource` (Task 8), controller (Task 7). `RestoreFieldName`/`RestoreField`/`ResolveRestore` consistent within Task 8.
