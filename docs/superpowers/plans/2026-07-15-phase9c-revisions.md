# Phase 9c — Revisions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-collection revision history with revert across the REST and GraphQL APIs: every create/update appends a complete, revert-capable JSON snapshot to a framework-owned `revisions` table, and a revert re-applies any past snapshot as the new current state (append-only).

**Architecture:** A collection opts in via `[CmsCollection(Revisions = true)]` (attribute flag like `AdminOnly` — nothing is added to the entity; snapshots live in a shared table). A new internal `Revision` entity + `IRevisionStore` (Application port) / `SqlSugarRevisionStore` (Infrastructure) own storage. A focused `RevisionSnapshotBuilder` assembles the canonical revert-capable shape (all `[CmsField]` values, M2O FK ids, M2M id arrays, all-locale raw translations). `ItemService` captures inside the existing write transaction on create/update; `RevertAsync` reuses the update core (tagged `revert`). REST exposes `GET .../{id}/revisions`, `GET .../{id}/revisions/{n}`, `POST .../{id}/revisions/{n}/revert`; GraphQL reaches parity via a shared `Revision` type, `xRevisions`/`xRevision` queries, and a `revertX` mutation per revisioned collection.

**Tech Stack:** .NET 10 / C# latest, SqlSugarCore, PostgreSQL (runtime) + SQLite (tests), ASP.NET Core Controllers, HotChocolate v16 (GraphQL), xUnit.

## Global Constraints

- All DB access via SqlSugar ORM; zero vendor SQL (§17.4). `MaxAsync`/`Insertable`/`Queryable` are idiomatic SqlSugar.
- Domain stays free of external packages; persistence attributes live on entities only (§2). The `Revision` entity lives in **Infrastructure** (like `File`), so `[SugarColumn]` is allowed on it.
- Framework code never references `samples/*` (§2). Only the sample `Article` opts in.
- Metadata scanned at startup and cached; no per-request reflection (§17.6).
- Outbound JSON = camelCase. Dictionary keys (KeyValue entries, translation locales) stay **verbatim** — `JsonSerializerDefaults.Web` does not rename dictionary keys.
- Response envelope stays the Phase 9a shape — revision endpoints return ordinary `ObjectResult`s the `EnvelopeResultFilter` wraps.
- Package versions never inferred from memory; **no new NuGet packages** are needed for this slice.
- The snapshot column MUST be `text` (set explicitly via `[SugarColumn(ColumnDataType = "text")]`) — SqlSugar's default `varchar(255)` overflows on Postgres (the recurring 7g/7g+ bug class).
- Capture must run **inside** the write transaction (`repository.InTransactionAsync`) so a snapshot and its write commit/roll back together.
- Acceptance: `dotnet build -warnaserror` → 0 warnings; `dotnet test` all green (635 baseline + new); frontend untouched (`pnpm test` stays 267); **live gate on real Postgres** before done.
- Spec: `docs/superpowers/specs/2026-07-14-phase9c-revisions-design.md`.

---

## File Structure

**Domain (`src/Struo.Domain`)**
- Modify `Metadata/Attributes/CmsCollectionAttribute.cs` — add `bool Revisions`.
- Modify `Metadata/Models/CollectionMetadata.cs` — add `bool Revisions { get; init; }`.

**Infrastructure (`src/Struo.Infrastructure`)**
- Create `Revisions/Revision.cs` — internal `[SugarTable("revisions")]` entity (NOT a `[CmsCollection]`).
- Create `Revisions/SqlSugarRevisionStore.cs` — the `IRevisionStore` implementation.
- Modify `Metadata/MetadataScanner.cs` — set `Revisions` from the attribute.
- Modify `Metadata/FrameworkEntityTypes.cs` — register `Revision` for `InitTables`.
- Modify `DependencyInjection/DataServiceCollectionExtensions.cs` — register `IRevisionStore` + `RevisionSnapshotBuilder`.

**Application (`src/Struo.Application`)**
- Create `Revisions/IRevisionStore.cs` — the port + `RevisionInfo`/`RevisionRecord` records.
- Create `Query/RevisionSnapshotBuilder.cs` — the canonical snapshot assembler.
- Modify `Query/ItemService.cs` — inject the store + builder; capture on create/update; `RevertAsync`/`ListRevisionsAsync`/`GetRevisionAsync`; refactor update body into a private core.

**Api (`src/Struo.Api`)**
- Modify `Controllers/ItemsController.cs` — three revision sub-routes.
- Modify `GraphQl/GraphQlDataSource.cs` — list/get/revert methods.
- Modify `GraphQl/SchemaTypeMapper.cs` — revision field-name helpers.
- Create `GraphQl/RevisionResolvers.cs` — query resolvers + the shared `Revision` object type.
- Modify `GraphQl/MutationResolvers.cs` — `RevertField`/`ResolveRevert`.
- Modify `GraphQl/StruoTypeModule.cs` — register the `Revision` type + per-revisioned-collection query/mutation fields.

**Sample (`samples/Struo.Sample.Blog`)**
- Modify `Article.cs` — add `Revisions = true` to `[CmsCollection]`.

**Migrations (`db/migrations`)**
- Create `006-revisions-table.sql`.

**Tests (`tests/Struo.Tests`)**
- Extend `Metadata/MetadataScannerTests.cs`.
- Create `Revisions/RevisionStoreTests.cs`, `Revisions/RevisionSnapshotBuilderTests.cs`, `Revisions/RevisionServiceTests.cs`, `Api/RevisionEndpointTests.cs`, `GraphQl/GraphQlRevisionTests.cs`.
- Extend the DDL-mapping test (`Persistence/StructuredColumnMappingTests.cs` or equivalent) with a `revisions.snapshot = text` assertion.

---

### Task 1: Metadata — `Revisions` attribute flag + `CollectionMetadata.Revisions` + scanner

**Files:**
- Modify: `src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs`
- Modify: `src/Struo.Domain/Metadata/Models/CollectionMetadata.cs`
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs:185-198` (`BuildCollection` return initializer)
- Test: `tests/Struo.Tests/Metadata/MetadataScannerTests.cs`

**Interfaces:**
- Produces: `CmsCollectionAttribute.Revisions` (bool); `CollectionMetadata.Revisions` (bool); scanner sets it from `attr.Revisions`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Metadata/MetadataScannerTests.cs — add (match the file's existing usings:
// SqlSugar, Struo.Domain.Metadata.Attributes, Struo.Domain.Metadata.Enums)
[Fact]
public void Scan_marks_revisioned_collection()
{
    var metas = MetadataScanner.ScanTypes([typeof(RevColl), typeof(PlainColl)]);
    Assert.True(metas.Single(m => m.Name == "revColl").Revisions);
    Assert.False(metas.Single(m => m.Name == "plainColl").Revisions);
}

[CmsCollection("RevColl", Revisions = true)]
private sealed class RevColl
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
}

[CmsCollection("PlainColl")]
private sealed class PlainColl
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Scan_marks_revisioned_collection"`
Expected: FAIL — `CmsCollectionAttribute`/`CollectionMetadata` have no `Revisions` member (compile error).

- [ ] **Step 3: Add the attribute flag**

```csharp
// src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs — add after AdminOnly:
/// <summary>
/// When true, the collection keeps a revision history: every successful create/update appends a
/// complete snapshot of the item's post-write state to the framework `revisions` table, and any past
/// revision can be re-applied via revert (Phase 9c). Opt-in; snapshots live in a shared table, so —
/// unlike soft delete — nothing is added to the entity, hence an attribute flag rather than an interface.
/// </summary>
public bool Revisions { get; set; }
```

- [ ] **Step 4: Add the metadata property**

```csharp
// src/Struo.Domain/Metadata/Models/CollectionMetadata.cs — add after SoftDelete:
/// <summary>
/// When true, the collection is revisioned: create/update append a snapshot to the `revisions` table
/// and a revert surface applies (Phase 9c). Derived by the scanner from `[CmsCollection(Revisions=true)]`.
/// </summary>
public bool Revisions { get; init; }
```

- [ ] **Step 5: Set it in the scanner**

```csharp
// src/Struo.Infrastructure/Metadata/MetadataScanner.cs — in BuildCollection's
// `return new CollectionMetadata { ... }`, add next to `AdminOnly = attr.AdminOnly,`:
Revisions = attr.Revisions,
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Scan_marks_revisioned_collection"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs src/Struo.Domain/Metadata/Models/CollectionMetadata.cs src/Struo.Infrastructure/Metadata/MetadataScanner.cs tests/Struo.Tests/Metadata/MetadataScannerTests.cs
git commit -m "feat(metadata): [CmsCollection(Revisions)] flag + CollectionMetadata.Revisions (9c)"
```

---

### Task 2: Revision entity + framework registration + DDL guard

**Files:**
- Create: `src/Struo.Infrastructure/Revisions/Revision.cs`
- Modify: `src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs:10-18`
- Test: `tests/Struo.Tests/Persistence/StructuredColumnMappingTests.cs` (extend — DDL assertion)

**Interfaces:**
- Produces: `Struo.Infrastructure.Revisions.Revision` entity (`Id`, `CollectionName`, `ItemId`, `RevisionNumber`, `Operation`, `Snapshot`, `CreatedAt`, `CreatedBy`); registered for `InitTables` via `FrameworkEntityTypes.All`.

- [ ] **Step 1: Write the failing test**

The repo already has a DDL-mapping test that runs `InitTables` on SQLite and asserts a column's data type via `client.DbMaintenance.GetColumnInfosByTableName(...)` (see the existing `text`-column assertions from 7g/7g+). Add a case for `revisions.snapshot`.

```csharp
// tests/Struo.Tests/Persistence/StructuredColumnMappingTests.cs — add
// (follow the file's existing client-build + InitTables harness; include the Revision type)
[Fact]
public void Revisions_snapshot_column_is_text()
{
    using var h = ColumnMappingHarness.Create(typeof(Struo.Infrastructure.Revisions.Revision));
    var cols = h.Client.DbMaintenance.GetColumnInfosByTableName("revisions", false);
    var snapshot = cols.Single(c => string.Equals(c.DbColumnName, "snapshot", StringComparison.OrdinalIgnoreCase));
    Assert.Contains("text", snapshot.DataType, StringComparison.OrdinalIgnoreCase);
}
```

> If the existing DDL test file uses a differently-named harness, mirror its exact construction (build an `ISqlSugarClient` via `SqlSugarClientFactory.Create` against the SQLite test connection, `InitTables(typeof(Revision))`, then read `DbMaintenance`). The single fact only needs the `revisions` table created and its `snapshot` column type read.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~Revisions_snapshot_column_is_text"`
Expected: FAIL — `Revision` type does not exist (compile error).

- [ ] **Step 3: Create the entity**

```csharp
// src/Struo.Infrastructure/Revisions/Revision.cs
using SqlSugar;

namespace Struo.Infrastructure.Revisions;

/// <summary>
/// One immutable snapshot of a revisioned item's post-write state (Phase 9c). An internal framework
/// table — NOT a <c>[CmsCollection]</c>, so it is never browsable/CRUD-able through the generic item
/// API. Append-only: rows are inserted on create/update/revert and never updated. Not
/// <see cref="Struo.Domain.Auditing.IAuditable"/> (no update path → no UpdatedAt/By); CreatedAt/By are
/// stamped explicitly at capture. Not <see cref="Struo.Domain.Auditing.ISoftDeletable"/>, so the global
/// soft-delete filter never touches it.
/// </summary>
[SugarTable("revisions")]
public sealed class Revision
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    public string CollectionName { get; set; } = "";
    public string ItemId { get; set; } = "";
    public long RevisionNumber { get; set; }
    public string Operation { get; set; } = "";

    // MUST be `text`: SqlSugar's default varchar(255) overflows on Postgres for a realistic snapshot
    // (the recurring 7g/7g+ bug class). Explicit here rather than via a convention.
    [SugarColumn(ColumnDataType = "text")] public string Snapshot { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? CreatedBy { get; set; }
}
```

- [ ] **Step 4: Register it for `InitTables`**

```csharp
// src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs — add to the `All` list (after UserRole):
typeof(Struo.Infrastructure.Revisions.Revision),
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~Revisions_snapshot_column_is_text"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Infrastructure/Revisions/Revision.cs src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs tests/Struo.Tests/Persistence/StructuredColumnMappingTests.cs
git commit -m "feat(infra): Revision entity (text snapshot) + InitTables registration (9c)"
```

---

### Task 3: `IRevisionStore` port + `SqlSugarRevisionStore` + DI

**Files:**
- Create: `src/Struo.Application/Revisions/IRevisionStore.cs`
- Create: `src/Struo.Infrastructure/Revisions/SqlSugarRevisionStore.cs`
- Modify: `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs:29`
- Test: `tests/Struo.Tests/Revisions/RevisionStoreTests.cs`

**Interfaces:**
- Consumes: `Revision` (Task 2); `ISqlSugarClient` + `ICurrentUserAccessor` (existing DI).
- Produces:
  - `record RevisionInfo(long RevisionNumber, string Operation, DateTime CreatedAt, Guid? CreatedBy)`
  - `record RevisionRecord(long RevisionNumber, string Operation, DateTime CreatedAt, Guid? CreatedBy, string Snapshot)`
  - `IRevisionStore.CaptureAsync(string collection, string itemId, string operation, string snapshotJson, CancellationToken)`
  - `IRevisionStore.ListAsync(string collection, string itemId, CancellationToken) : Task<IReadOnlyList<RevisionInfo>>` (newest-first)
  - `IRevisionStore.GetAsync(string collection, string itemId, long revisionNumber, CancellationToken) : Task<RevisionRecord?>`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Revisions/RevisionStoreTests.cs
// Build the client via SqlSugarClientFactory.Create against the SQLite test DB and InitTables(typeof(Revision)).
// Mirror the client/fixture construction used by SqlSugarItemRepositoryTests.
using Struo.Application.Revisions;
using Xunit;

namespace Struo.Tests.Revisions;

public sealed class RevisionStoreTests
{
    [Fact]
    public async Task Capture_assigns_monotonic_per_item_numbers()
    {
        using var h = RevisionStoreHarness.Create();
        await h.Store.CaptureAsync("article", "itemA", "create", "{\"a\":1}", default);
        await h.Store.CaptureAsync("article", "itemA", "update", "{\"a\":2}", default);
        await h.Store.CaptureAsync("article", "itemB", "create", "{\"b\":1}", default); // separate item -> its own 1

        var a = await h.Store.ListAsync("article", "itemA", default);
        Assert.Equal([2L, 1L], a.Select(r => r.RevisionNumber).ToArray());   // newest-first
        Assert.Equal("update", a[0].Operation);

        var b = await h.Store.ListAsync("article", "itemB", default);
        Assert.Single(b);
        Assert.Equal(1L, b[0].RevisionNumber);                               // per-item sequence, not global
    }

    [Fact]
    public async Task Get_returns_snapshot_with_cjk_intact()
    {
        using var h = RevisionStoreHarness.Create();
        await h.Store.CaptureAsync("article", "x", "create", "{\"title\":\"人工智慧\"}", default);
        var rec = await h.Store.GetAsync("article", "x", 1, default);
        Assert.NotNull(rec);
        Assert.Contains("人工智慧", rec!.Snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_unknown_returns_null()
    {
        using var h = RevisionStoreHarness.Create();
        Assert.Null(await h.Store.GetAsync("article", "nope", 99, default));
    }
}
```

> Implement `RevisionStoreHarness` in the test file: build an `ISqlSugarClient` via `SqlSugarClientFactory.Create(...)` over the SQLite test connection (copy from `SqlSugarItemRepositoryTests`), `client.CodeFirst.InitTables(typeof(Revision))`, and construct `new SqlSugarRevisionStore(client, new TestCurrentUserAccessor(...))` (reuse the `TestCurrentUserAccessor` support type). Expose `.Store`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~RevisionStoreTests"`
Expected: FAIL — `IRevisionStore`/`SqlSugarRevisionStore` do not exist (compile error).

- [ ] **Step 3: Write the port**

```csharp
// src/Struo.Application/Revisions/IRevisionStore.cs
namespace Struo.Application.Revisions;

/// <summary>Revision metadata (no snapshot payload) — for the newest-first history list.</summary>
public sealed record RevisionInfo(long RevisionNumber, string Operation, DateTime CreatedAt, Guid? CreatedBy);

/// <summary>A single revision including its stored snapshot JSON.</summary>
public sealed record RevisionRecord(long RevisionNumber, string Operation, DateTime CreatedAt, Guid? CreatedBy, string Snapshot);

/// <summary>
/// Storage for per-item revision snapshots (Phase 9c). Backed by the framework `revisions` table.
/// Implementations run on the request-scoped SqlSugar client, so <see cref="CaptureAsync"/> called
/// inside <c>ItemService</c>'s write transaction commits atomically with the write it describes.
/// </summary>
public interface IRevisionStore
{
    /// Assigns the next per-(collection,itemId) RevisionNumber, stamps CreatedAt/By, inserts the snapshot.
    Task CaptureAsync(string collection, string itemId, string operation, string snapshotJson, CancellationToken ct = default);

    /// Newest-first metadata (no snapshot). Empty when the item has no revisions.
    Task<IReadOnlyList<RevisionInfo>> ListAsync(string collection, string itemId, CancellationToken ct = default);

    /// One revision incl. snapshot, or null when (collection,itemId,revisionNumber) has no row.
    Task<RevisionRecord?> GetAsync(string collection, string itemId, long revisionNumber, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the implementation**

```csharp
// src/Struo.Infrastructure/Revisions/SqlSugarRevisionStore.cs
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Revisions;

namespace Struo.Infrastructure.Revisions;

public sealed class SqlSugarRevisionStore(ISqlSugarClient db, ICurrentUserAccessor currentUser) : IRevisionStore
{
    public async Task CaptureAsync(string collection, string itemId, string operation, string snapshotJson, CancellationToken ct = default)
    {
        // Next per-item sequence. Inside ItemService's write transaction the single-item write path is
        // serialized, so max+1 is race-free here. MaxAsync over no rows returns null -> 0.
        var max = await db.Queryable<Revision>()
            .Where(r => r.CollectionName == collection && r.ItemId == itemId)
            .MaxAsync(r => (long?)r.RevisionNumber);

        var row = new Revision
        {
            Id = Guid.CreateVersion7(),
            CollectionName = collection,
            ItemId = itemId,
            RevisionNumber = (max ?? 0) + 1,
            Operation = operation,
            Snapshot = snapshotJson,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUser.GetCurrentUserId()
        };
        await db.Insertable(row).ExecuteCommandAsync();
    }

    public async Task<IReadOnlyList<RevisionInfo>> ListAsync(string collection, string itemId, CancellationToken ct = default)
    {
        var rows = await db.Queryable<Revision>()
            .Where(r => r.CollectionName == collection && r.ItemId == itemId)
            .OrderBy(r => r.RevisionNumber, OrderByType.Desc)
            .ToListAsync();
        return rows.Select(r => new RevisionInfo(r.RevisionNumber, r.Operation, r.CreatedAt, r.CreatedBy)).ToList();
    }

    public async Task<RevisionRecord?> GetAsync(string collection, string itemId, long revisionNumber, CancellationToken ct = default)
    {
        var r = await db.Queryable<Revision>()
            .Where(x => x.CollectionName == collection && x.ItemId == itemId && x.RevisionNumber == revisionNumber)
            .FirstAsync();
        return r is null ? null : new RevisionRecord(r.RevisionNumber, r.Operation, r.CreatedAt, r.CreatedBy, r.Snapshot);
    }
}
```

- [ ] **Step 5: Register in DI**

```csharp
// src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs
// after services.AddSingleton<IHtmlSanitizer, GanssHtmlSanitizer>(); and before services.AddScoped<ItemService>();
services.AddScoped<Struo.Application.Revisions.IRevisionStore, Struo.Infrastructure.Revisions.SqlSugarRevisionStore>();
```

> `RevisionSnapshotBuilder` (Task 4) is registered in Task 4's step; `ItemService` gains the two new deps in Task 5.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~RevisionStoreTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Application/Revisions/IRevisionStore.cs src/Struo.Infrastructure/Revisions/SqlSugarRevisionStore.cs src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs tests/Struo.Tests/Revisions/RevisionStoreTests.cs
git commit -m "feat(revisions): IRevisionStore + SqlSugarRevisionStore (monotonic per-item) (9c)"
```

---

### Task 4: Canonical snapshot builder

**Files:**
- Create: `src/Struo.Application/Query/RevisionSnapshotBuilder.cs`
- Modify: `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs`
- Test: `tests/Struo.Tests/Revisions/RevisionSnapshotBuilderTests.cs`

**Interfaces:**
- Consumes: `IItemRepository`, `IMetadataProvider`, `IEntityRegistry`, `IM2MDescriptorSource` (all existing).
- Produces: `RevisionSnapshotBuilder.BuildAsync(string collection, object entity, CancellationToken) : Task<string>` — a canonical, revert-capable JSON string containing every `[CmsField]` value (raw, no RBAC filtering), each M2O FK id under its camel FK name, each M2M relation as an ordered id array under its relation name, all-locale `translations`, and `version`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Revisions/RevisionSnapshotBuilderTests.cs
// Use the ItemService integration harness (client + registry + metadata + repository over the sample
// Blog types) — see DeepExpansionTests / RelationWriteTests for the wiring. Create a live Article with
// a category (M2O), tags (M2M), regions (multi-value), and en+zh-TW translations, then build a snapshot.
using System.Text.Json;
using Xunit;

namespace Struo.Tests.Revisions;

public sealed class RevisionSnapshotBuilderTests
{
    [Fact]
    public async Task Snapshot_is_revert_capable()
    {
        using var h = SnapshotBuilderHarness.Create();               // sample Blog types, live SQLite
        var (articleId, categoryId, tagId) = await h.SeedArticleWithRelationsAndI18nAsync();
        var entity = await h.Repository.GetByIdAsync("article", articleId.ToString(), default);

        var json = await h.Builder.BuildAsync("article", entity!, default);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // M2O FK id present under camel FK name (NOT in the read projection).
        Assert.Equal(categoryId.ToString(), root.GetProperty("categoryId").GetString());
        // M2M relation as an ordered id array under the relation name.
        Assert.Equal(tagId.ToString(), root.GetProperty("tags")[0].GetString());
        // Multi-value own-field preserved.
        Assert.Contains("apac", root.GetProperty("regions").EnumerateArray().Select(e => e.GetString()));
        // All-locale translations, raw values, verbatim locale keys.
        Assert.True(root.GetProperty("translations").TryGetProperty("zh-TW", out var zh));
        Assert.Equal("人工智慧", zh.GetProperty("title").GetString());   // CJK code-point-exact
        // version captured for reference.
        Assert.True(root.TryGetProperty("version", out _));
    }
}
```

> Implement `SnapshotBuilderHarness` mirroring the existing `ItemService`-integration harnesses: build the `ISqlSugarClient` + `IItemRepository` (`SqlSugarItemRepository`) + `IMetadataProvider` (`CachedMetadataProvider` over the sample assembly) + `IEntityRegistry` + `IM2MDescriptorSource`, `InitTables` the sample types, and expose `.Repository` and `.Builder = new RevisionSnapshotBuilder(repository, metadata, registry, m2mSource)`. `SeedArticleWithRelationsAndI18nAsync` creates a category + tag then an article (via `ItemService.CreateAsync` with `category`/`tags`/`regions`/`translations`) and returns the ids.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~RevisionSnapshotBuilderTests"`
Expected: FAIL — `RevisionSnapshotBuilder` does not exist (compile error).

- [ ] **Step 3: Write the builder**

```csharp
// src/Struo.Application/Query/RevisionSnapshotBuilder.cs
using System.Reflection;
using System.Text.Json;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>
/// Assembles a canonical, revert-capable JSON snapshot of an item's post-write state (Phase 9c). Unlike
/// the read projection (<c>ItemService.Project</c>), it (a) applies NO RBAC field filtering — a snapshot
/// must capture the whole item regardless of the writer's field grants — and (b) includes M2O foreign-key
/// ids and M2M relation id arrays, so the result is exactly the shape <c>ItemService.UpdateAsync</c>
/// consumes and revert round-trips through the normal write path.
/// </summary>
public sealed class RevisionSnapshotBuilder(
    IItemRepository repository,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IM2MDescriptorSource m2mSource)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<string> BuildAsync(string collection, object entity, CancellationToken ct = default)
    {
        var meta = metadata.GetCollection(collection)
                   ?? throw new CollectionNotFoundException(collection);
        var d = registry.Get(collection)!;
        var snap = new Dictionary<string, object?>();

        var idValue = d.EntityType.GetProperty(d.IdProperty)?.GetValue(entity);
        snap["id"] = idValue;
        if (entity is Struo.Domain.Auditing.AuditableEntity versioned)
            snap["version"] = versioned.Version;

        // (1) All [CmsField] own-field values — no RBAC filtering, no hidden-skip. Json stays raw text
        //     (parsed to a structured JsonElement so it serialises as JSON, not a quoted string).
        foreach (var field in meta.Fields)
        {
            if (field.IsSystem) continue;                 // audit fields are server-managed, not part of a revert body
            if (field.Translatable) continue;             // translatable own-fields live in `translations` below
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var value = d.EntityType.GetProperty(prop)?.GetValue(entity);
            if (field.Interface == FieldInterface.Json && value is string rawJson)
            {
                try { value = JsonSerializer.Deserialize<JsonElement>(rawJson); }
                catch (JsonException) { /* leave raw string; defensive, unreachable via the write path */ }
            }
            snap[field.Name] = value;
        }

        // (2) M2O foreign-key ids (declared via [CmsRelation], so absent from meta.Fields) under the
        //     camel FK name UpdateAsync overlays (e.g. "categoryId").
        foreach (var rel in meta.Relations)
        {
            if (rel.Kind != RelationKind.ManyToOne || rel.ForeignKey is null) continue;
            var pi = d.EntityType.GetProperty(rel.ForeignKey,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            snap[rel.ForeignKey] = pi?.GetValue(entity);
        }

        // (3) M2M relations as ordered id arrays under the relation name (e.g. "tags": ["<id>", ...]).
        foreach (var desc in m2mSource.M2MDescriptors(collection))
        {
            var junctions = await repository.QueryEntityWhereInAsync(
                desc.JunctionType, desc.ParentFkProperty, [idValue!], ct);
            var ordered = desc.SortProperty is null
                ? junctions
                : junctions.OrderBy(j => ReadProp(j, desc.SortProperty)).ToList();
            snap[desc.RelationName] = ordered
                .Select(j => ReadProp(j, desc.TargetFkProperty))
                .ToList();
        }

        // (4) All-locale translations: { locale: { camelField: rawValue } }. Image/file fields stay
        //     raw ids (they round-trip through the write path). Verbatim locale keys.
        var tm = meta.Translation;
        if (tm is not null && idValue is not null)
        {
            var tRows = await repository.LoadTranslationsAsync(
                tm.TranslationEntityType, tm.ForeignKeyProperty, tm.LocaleProperty, [idValue], null, ct);
            var camelToClr = tm.Fields.ToDictionary(
                f => f,
                f => tm.TranslationEntityType.GetProperty(f,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.Name ?? f,
                StringComparer.OrdinalIgnoreCase);

            var byLocale = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tr in tRows)
            {
                if (ReadProp(tr, tm.LocaleProperty) is not string loc) continue;
                var fieldMap = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (camel, clr) in camelToClr) fieldMap[camel] = ReadProp(tr, clr);
                byLocale[loc] = fieldMap;
            }
            snap["translations"] = byLocale;
        }

        return JsonSerializer.Serialize(snap, JsonOpts);
    }

    private static object? ReadProp(object o, string name) =>
        o.GetType().GetProperty(name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(o);
}
```

- [ ] **Step 4: Register in DI**

```csharp
// src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs
// next to the IRevisionStore registration (Task 3):
services.AddScoped<Struo.Application.Query.RevisionSnapshotBuilder>();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~RevisionSnapshotBuilderTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Application/Query/RevisionSnapshotBuilder.cs src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs tests/Struo.Tests/Revisions/RevisionSnapshotBuilderTests.cs
git commit -m "feat(revisions): canonical revert-capable snapshot builder (9c)"
```

---

### Task 5: `ItemService` — capture on create/update (in-transaction) + update-core refactor

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs` (constructor; `CreateAsync`; `UpdateAsync` → private `UpdateCoreAsync`)
- Test: `tests/Struo.Tests/Revisions/RevisionServiceTests.cs`

**Interfaces:**
- Consumes: `IRevisionStore` (Task 3), `RevisionSnapshotBuilder` (Task 4), `CollectionMetadata.Revisions` (Task 1).
- Produces: create/update append a revision when `meta.Revisions`; `UpdateAsync` delegates to a private `UpdateCoreAsync(collection, id, body, string operation, ct)` (public signature unchanged) that Task 6's `RevertAsync` reuses with `operation: "revert"`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Struo.Tests/Revisions/RevisionServiceTests.cs
// Use an ItemService integration harness over the sample Blog types (Article is revisioned in Task 8;
// for this task, add Revisions=true to Article FIRST as part of Step 3, OR drive a purpose-built
// revisioned fixture collection). Simplest: this task's tests assume Article is revisioned — do Task 8's
// one-line sample edit here in Step 3 so create/update capture is exercised end-to-end.
using Struo.Application.Revisions;
using Xunit;

namespace Struo.Tests.Revisions;

public sealed class RevisionServiceTests
{
    [Fact]
    public async Task Create_then_update_appends_two_revisions()
    {
        using var h = RevisionServiceHarness.Create();
        var id = await h.CreateArticleAsync(status: "draft");        // create -> rev 1
        await h.Service.UpdateAsync("article", id, h.Body(status: "published"), default); // update -> rev 2

        var list = await h.Store.ListAsync("article", id, default);
        Assert.Equal(2, list.Count);
        Assert.Equal("update", list[0].Operation);                   // newest-first
        Assert.Equal("create", list[1].Operation);
    }

    [Fact]
    public async Task Non_revisioned_collection_captures_nothing()
    {
        using var h = RevisionServiceHarness.Create();
        var id = await h.CreateCategoryAsync(name: "Cat");           // Category is NOT revisioned
        var list = await h.Store.ListAsync("category", id, default);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Capture_failure_rolls_back_the_write()
    {
        using var h = RevisionServiceHarness.Create(failCapture: true); // store stub throws in CaptureAsync
        await Assert.ThrowsAnyAsync<Exception>(() => h.CreateArticleTask(status: "draft"));
        // the parent write rolled back with the failed capture (same transaction) -> no article persisted
        var page = await h.Service.QueryAsync("article", new QueryModel(null, null, [], 100, 0, null), null, default);
        Assert.Empty(page.Data);
    }
}
```

> `RevisionServiceHarness` builds a real `ItemService` (client/registry/metadata/permissions/expander/relationFilter/languages/options/sanitizer/currentUser) plus the real `SqlSugarRevisionStore` + `RevisionSnapshotBuilder` (see the `SnapshotBuilderHarness` wiring from Task 4 and the `ItemService` construction in existing integration tests). `failCapture: true` swaps in a tiny `IRevisionStore` stub whose `CaptureAsync` throws. `Body(...)` returns a `JsonElement` update body; `CreateArticleAsync` posts a create body with the required default-locale `translations`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~RevisionServiceTests"`
Expected: FAIL — no capture happens (list empty), and `Article` is not yet revisioned.

- [ ] **Step 3: Add the sample opt-in + inject the deps**

```csharp
// samples/Struo.Sample.Blog/Article.cs — change the [CmsCollection] attribute to add Revisions = true:
[CmsCollection("Article", Icon = "article", Group = "Content", DefaultDisplayField = nameof(Status), Revisions = true)]
```

```csharp
// src/Struo.Application/Query/ItemService.cs — add two params to the primary constructor
// (after `ICurrentUserAccessor currentUser`):
    IRevisionStore revisions,
    RevisionSnapshotBuilder snapshotBuilder)
```
Add `using Struo.Application.Revisions;` to the file's usings.

- [ ] **Step 4: Capture on create**

```csharp
// ItemService.CreateAsync — inside the existing repository.InTransactionAsync lambda, AFTER the
// SyncTranslationsAsync call and BEFORE the lambda closes, add:
            if (meta.Revisions)
            {
                var snapshot = await snapshotBuilder.BuildAsync(collection, created, ct);
                await revisions.CaptureAsync(collection, createdId.ToString()!, "create", snapshot, ct);
            }
```
(`createdId` is already computed in the lambda.)

- [ ] **Step 5: Refactor `UpdateAsync` into a private core carrying the operation label**

```csharp
// ItemService — keep the PUBLIC signature; delegate to a private core:
public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
    string collection, string id, JsonElement body, CancellationToken ct = default)
    => UpdateCoreAsync(collection, id, body, "update", ct);

// Rename the current UpdateAsync body to UpdateCoreAsync with the extra `operation` parameter:
private async Task<IReadOnlyDictionary<string, object?>?> UpdateCoreAsync(
    string collection, string id, JsonElement body, string operation, CancellationToken ct)
{
    // ... existing UpdateAsync body UNCHANGED up to and including the InTransactionAsync block ...
    // inside the InTransactionAsync lambda, AFTER SyncTranslationsAsync and the `if (updated is null) return;`
    // early-out is already handled, add before the lambda closes:
            if (meta.Revisions)
            {
                var snapshot = await snapshotBuilder.BuildAsync(collection, updated!, ct);
                await revisions.CaptureAsync(collection, updatedId.ToString()!, operation, snapshot, ct);
            }
    // ... rest UNCHANGED ...
}
```

> The capture sits inside `InTransactionAsync`, so a `CaptureAsync` throw rolls back the parent write (Step 1's third test). `updated`/`created` carry the just-written parent fields + FK; the builder re-reads M2M + translations (committed within the transaction) itself.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~RevisionServiceTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Full build + no regressions**

Run: `dotnet build -warnaserror && dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemService|FullyQualifiedName~SqlSugarItemRepositoryTests"`
Expected: 0 warnings; existing write-path tests still green (capture is additive + gated on `meta.Revisions`).

- [ ] **Step 8: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs samples/Struo.Sample.Blog/Article.cs tests/Struo.Tests/Revisions/RevisionServiceTests.cs
git commit -m "feat(app): capture revisions in the write transaction; Article opts in (9c)"
```

---

### Task 6: `ItemService` — `RevertAsync` + `ListRevisionsAsync` + `GetRevisionAsync`

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs`
- Test: `tests/Struo.Tests/Revisions/RevisionServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `UpdateCoreAsync` (Task 5), `IRevisionStore` (Task 3), `StripKeys` (existing private helper).
- Produces:
  - `RevertAsync(string collection, string id, long revisionNumber, CancellationToken) : Task<IReadOnlyDictionary<string,object?>?>` (null → 404)
  - `ListRevisionsAsync(string collection, string id, CancellationToken) : Task<IReadOnlyList<RevisionInfo>>` (CanRead)
  - `GetRevisionAsync(string collection, string id, long revisionNumber, CancellationToken) : Task<RevisionRecord?>` (CanRead)

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Struo.Tests/Revisions/RevisionServiceTests.cs — add
[Fact]
public async Task Revert_reapplies_snapshot_and_appends_revert_revision()
{
    using var h = RevisionServiceHarness.Create();
    var id = await h.CreateArticleAsync(status: "draft");                       // rev 1 (create), status=draft
    await h.Service.UpdateAsync("article", id, h.Body(status: "published"), default); // rev 2, status=published

    var reverted = await h.Service.RevertAsync("article", id, 1, default);      // revert to rev 1 (draft)
    Assert.NotNull(reverted);
    Assert.Equal("draft", reverted!["status"]);                                 // current state matches rev 1

    var list = await h.Service.ListRevisionsAsync("article", id, default);
    Assert.Equal(3, list.Count);                                                // append-only: 1,2 preserved + revert
    Assert.Equal("revert", list[0].Operation);
}

[Fact]
public async Task Revert_restores_relations_and_translations()
{
    using var h = RevisionServiceHarness.Create();
    var (id, catA, catB, tag) = await h.SeedArticleForRevertAsync();            // rev1: category=catA, tags=[tag], zh-TW title
    await h.Service.UpdateAsync("article", id, h.Body(category: catB, tags: []), default); // rev2: catB, no tags

    await h.Service.RevertAsync("article", id, 1, default);
    var now = await h.Service.GetAsync("article", id, DeepFor("category", "tags"), null, default);
    Assert.Equal(catA.ToString(), CategoryIdOf(now!));                          // M2O FK restored
    Assert.Single(TagsOf(now!));                                                // M2M restored
    Assert.Equal(tag.ToString(), TagIdOf(now!, 0));
}

[Fact]
public async Task Revert_unknown_revision_returns_null()
{
    using var h = RevisionServiceHarness.Create();
    var id = await h.CreateArticleAsync(status: "draft");
    Assert.Null(await h.Service.RevertAsync("article", id, 99, default));       // -> 404
}

[Fact]
public async Task GetRevision_returns_snapshot_and_list_is_read_gated()
{
    using var h = RevisionServiceHarness.Create();
    var id = await h.CreateArticleAsync(status: "draft");
    var rec = await h.Service.GetRevisionAsync("article", id, 1, default);
    Assert.NotNull(rec);
    Assert.Contains("\"status\"", rec!.Snapshot, StringComparison.Ordinal);

    using var noRead = RevisionServiceHarness.Create(canRead: false);
    await Assert.ThrowsAsync<PermissionDeniedException>(
        () => noRead.Service.ListRevisionsAsync("article", id, default));
}
```

> Extend the harness with `SeedArticleForRevertAsync`, `DeepFor(...)` (builds a `DeepSpec` for `category`/`tags`), and the small `CategoryIdOf`/`TagsOf`/`TagIdOf` projection readers (the projected dict nests `category`/`tags` when deep-expanded). `canRead: false` uses a permission service that denies `CanRead`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~RevisionServiceTests"`
Expected: FAIL — `RevertAsync`/`ListRevisionsAsync`/`GetRevisionAsync` don't exist (compile error).

- [ ] **Step 3: Add the three methods**

```csharp
// src/Struo.Application/Query/ItemService.cs — add:

/// <summary>
/// Reverts an item to a past revision by re-applying that revision's snapshot as a normal update
/// (append-only: a new "revert" revision is recorded; forward history is never deleted). Returns the
/// re-read item, or null for an unknown collection-revision/item (→ 404). Requires write permission.
/// </summary>
public async Task<IReadOnlyDictionary<string, object?>?> RevertAsync(
    string collection, string id, long revisionNumber, CancellationToken ct = default)
{
    var meta = Meta(collection);
    if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
    RequireSuperAdminForAdminOnly(meta);
    if (!meta.Revisions) return null;                                   // collection keeps no revisions -> 404

    var rec = await revisions.GetAsync(collection, id, revisionNumber, ct);
    if (rec is null) return null;                                       // unknown revision/item -> 404

    // The snapshot IS a valid update body by construction; drop `version` so revert does not echo a
    // stale optimistic-concurrency token (it would 409 against the current row). Keep the JsonDocument
    // alive across the awaited update (the body's JsonElement must stay valid).
    using var doc = JsonDocument.Parse(
        StripKeys(JsonDocument.Parse(rec.Snapshot).RootElement, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "version" }));
    return await UpdateCoreAsync(collection, id, doc.RootElement, "revert", ct);
}

/// <summary>Newest-first revision metadata for an item. Requires read permission. Empty for a
/// non-revisioned collection.</summary>
public async Task<IReadOnlyList<Struo.Application.Revisions.RevisionInfo>> ListRevisionsAsync(
    string collection, string id, CancellationToken ct = default)
{
    var meta = Meta(collection);
    if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
    if (!meta.Revisions) return [];
    return await revisions.ListAsync(collection, id, ct);
}

/// <summary>A single revision incl. its snapshot. Requires read permission. Null for an
/// unknown revision or a non-revisioned collection (→ 404).</summary>
public async Task<Struo.Application.Revisions.RevisionRecord?> GetRevisionAsync(
    string collection, string id, long revisionNumber, CancellationToken ct = default)
{
    var meta = Meta(collection);
    if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
    if (!meta.Revisions) return null;
    return await revisions.GetAsync(collection, id, revisionNumber, ct);
}
```

> The inner `JsonDocument.Parse(rec.Snapshot)` is parsed only to feed `StripKeys` (which enumerates the root object and re-serialises without `version`); its buffer is released when `StripKeys` returns. The outer `using var doc` owns the stripped body for the duration of `UpdateCoreAsync`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~RevisionServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Revisions/RevisionServiceTests.cs
git commit -m "feat(app): ItemService revert (append-only) + list/get revisions (9c)"
```

---

### Task 7: REST — revision list/get/revert endpoints

**Files:**
- Modify: `src/Struo.Api/Controllers/ItemsController.cs`
- Test: `tests/Struo.Tests/Api/RevisionEndpointTests.cs`

**Interfaces:**
- Consumes: `ItemService.ListRevisionsAsync`/`GetRevisionAsync`/`RevertAsync` (Task 6).
- Produces: `GET .../{id}/revisions`, `GET .../{id}/revisions/{n}`, `POST .../{id}/revisions/{n}/revert`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Struo.Tests/Api/RevisionEndpointTests.cs — use the ApiFactory harness (see ItemsEndpointTests /
// SoftDeleteEndpointTests for auth + JSON helpers). Envelope: success payloads read under `data`.
[Fact]
public async Task Revisions_lifecycle_over_rest()
{
    // create article (en default translation) -> 201
    // PUT status change -> 200
    // GET  /api/items/article/{id}/revisions        -> data has 2 entries, newest-first, operations update+create
    // GET  /api/items/article/{id}/revisions/1      -> data.snapshot present (structured JSON)
    // POST /api/items/article/{id}/revisions/1/revert -> 200; GET item shows the reverted status
    // GET  /api/items/article/{id}/revisions        -> now 3 entries (append-only; newest operation "revert")
}

[Fact]
public async Task Get_unknown_revision_is_404()
{
    // GET /api/items/article/{id}/revisions/999 -> 404
}

[Fact]
public async Task Revert_requires_write_permission()
{
    // as a read-only user: POST .../revisions/1/revert -> 403 (or 401 anonymous)
}
```

> Fill bodies following `ItemsEndpointTests`/`SoftDeleteEndpointTests` (authenticated create with `translations`, status assertions). Use the bootstrap super-admin for happy paths; a write-less role for the 403 (see `RbacEnforcementTests`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~RevisionEndpointTests"`
Expected: FAIL — routes return 404 (not implemented).

- [ ] **Step 3: Add the endpoints**

```csharp
// src/Struo.Api/Controllers/ItemsController.cs — add these actions (System.Text.Json is already imported):

[HttpGet("{id}/revisions")]
public async Task<IActionResult> Revisions(string collection, string id, CancellationToken ct)
{
    var list = await items.ListRevisionsAsync(collection, id, ct);
    return Ok(list);   // EnvelopeResultFilter wraps -> { success, data: [ { revisionNumber, operation, createdAt, createdBy } ] }
}

[HttpGet("{id}/revisions/{revisionNumber:long}")]
public async Task<IActionResult> Revision(string collection, string id, long revisionNumber, CancellationToken ct)
{
    var rec = await items.GetRevisionAsync(collection, id, revisionNumber, ct);
    if (rec is null) return NotFound();
    // Emit the stored snapshot as structured JSON (not a quoted string).
    using var snapshotDoc = JsonDocument.Parse(rec.Snapshot);
    return Ok(new
    {
        rec.RevisionNumber,
        rec.Operation,
        rec.CreatedAt,
        rec.CreatedBy,
        snapshot = snapshotDoc.RootElement.Clone()   // Clone so the value survives the using-scope dispose
    });
}

[HttpPost("{id}/revisions/{revisionNumber:long}/revert")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public async Task<IActionResult> Revert(string collection, string id, long revisionNumber, CancellationToken ct)
{
    var reverted = await items.RevertAsync(collection, id, revisionNumber, ct);
    return reverted is null ? NotFound() : Ok(reverted);
}
```

> `JsonElement.Clone()` detaches the value from `snapshotDoc` so it remains valid after the `using` disposes — the serializer runs later, in the result filter. No controller ctor change (only `ItemService` is used).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~RevisionEndpointTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Controllers/ItemsController.cs tests/Struo.Tests/Api/RevisionEndpointTests.cs
git commit -m "feat(api): REST revision list/get/revert endpoints (9c)"
```

---

### Task 8: GraphQL — `Revision` type + `xRevisions`/`xRevision` queries + `revertX` mutation

**Files:**
- Modify: `src/Struo.Api/GraphQl/GraphQlDataSource.cs`
- Modify: `src/Struo.Api/GraphQl/SchemaTypeMapper.cs`
- Create: `src/Struo.Api/GraphQl/RevisionResolvers.cs`
- Modify: `src/Struo.Api/GraphQl/MutationResolvers.cs`
- Modify: `src/Struo.Api/GraphQl/StruoTypeModule.cs`
- Test: `tests/Struo.Tests/GraphQl/GraphQlRevisionTests.cs`

**Interfaces:**
- Consumes: `ItemService` revision methods (Task 6), existing resolver/name-mapper patterns.
- Produces: shared `Revision` object type (`revisionNumber:Int!`, `operation:String!`, `createdAt:DateTime!`, `createdBy:ID`, `snapshot:Any`); `xRevisions(id:ID!):[Revision!]!`, `xRevision(id:ID!, revisionNumber:Int!):Revision`, `revertX(id:ID!, revisionNumber:Int!):X` — only for revisioned collections.

- [ ] **Step 1: Extend the data-source seam**

```csharp
// src/Struo.Api/GraphQl/GraphQlDataSource.cs — add to the interface:
Task<IReadOnlyList<Struo.Application.Revisions.RevisionInfo>> ListRevisionsAsync(string collection, string id, CancellationToken ct);
Task<Struo.Application.Revisions.RevisionRecord?> GetRevisionAsync(string collection, string id, long revisionNumber, CancellationToken ct);
Task<IReadOnlyDictionary<string, object?>?> RevertAsync(string collection, string id, long revisionNumber, CancellationToken ct);

// ItemServiceGraphQlDataSource — implement:
public Task<IReadOnlyList<Struo.Application.Revisions.RevisionInfo>> ListRevisionsAsync(string collection, string id, CancellationToken ct)
    => items.ListRevisionsAsync(collection, id, ct);
public Task<Struo.Application.Revisions.RevisionRecord?> GetRevisionAsync(string collection, string id, long revisionNumber, CancellationToken ct)
    => items.GetRevisionAsync(collection, id, revisionNumber, ct);
public Task<IReadOnlyDictionary<string, object?>?> RevertAsync(string collection, string id, long revisionNumber, CancellationToken ct)
    => items.RevertAsync(collection, id, revisionNumber, ct);
```

> Update `tests/Struo.Tests/GraphQl/FakeGraphQlDataSource.cs` (if present) to implement the three new members.

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Struo.Tests/GraphQl/GraphQlRevisionTests.cs — use the GraphQlEndpointTests harness pattern.
[Fact]
public async Task ArticleRevisions_lists_and_revertArticle_reapplies()
{
    // createArticle (en) -> updateArticle(status) -> query { articleRevisions(id){ revisionNumber operation } }
    // -> 2 entries newest-first; mutation { revertArticle(id, revisionNumber:1){ status } } -> reverted status;
    // articleRevisions again -> 3 (newest operation "revert").
}

[Fact]
public async Task ArticleRevision_returns_snapshot_any()
{
    // query { articleRevision(id, revisionNumber:1){ operation snapshot } } -> snapshot is a JSON object
}

[Fact]
public async Task RevertArticle_unknown_revision_is_null()
{
    // mutation { revertArticle(id, revisionNumber:999){ id } } -> data.revertArticle == null
}
```

> Drive over `/graphql` via `ApiFactory` (see `GraphQlEndpointTests`/`GraphQlMutationExecutionTests`). `Article` is revisioned (Task 5). Assert `data`/`errors[].extensions.code` as those tests do.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlRevisionTests"`
Expected: FAIL — `articleRevisions`/`articleRevision`/`revertArticle` not in the schema.

- [ ] **Step 4: Add name helpers**

```csharp
// src/Struo.Api/GraphQl/SchemaTypeMapper.cs — add near RestoreFieldName:
public static string RevisionsFieldName(string collection) => Camel(collection) + "Revisions";
public static string RevisionFieldName(string collection) => Camel(collection) + "Revision";
public static string RevertFieldName(string collection) => "revert" + Pascal(collection);
```

- [ ] **Step 5: Add the shared `Revision` type + query resolvers**

```csharp
// src/Struo.Api/GraphQl/RevisionResolvers.cs
using System.Text.Json;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Revisions;

namespace Struo.Api.GraphQl;

/// <summary>
/// The shared `Revision` object type + per-collection `xRevisions`/`xRevision` query field configs.
/// A Revision node is represented as an IReadOnlyDictionary (like Translation) so the fields resolve
/// uniformly. `snapshot` is null on the list (metadata only) and populated by the single query —
/// mirroring REST (list = metadata, get = snapshot).
/// </summary>
internal static class RevisionResolvers
{
    internal static ObjectType RevisionType()
    {
        var config = new ObjectTypeConfiguration("Revision", null, typeof(IReadOnlyDictionary<string, object?>));
        config.Fields.Add(CollectionSchemaBuilder.Field("revisionNumber", "Int!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("revisionNumber")));
        config.Fields.Add(CollectionSchemaBuilder.Field("operation", "String!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("operation")));
        config.Fields.Add(CollectionSchemaBuilder.Field("createdAt", "DateTime!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("createdAt")));
        config.Fields.Add(CollectionSchemaBuilder.Field("createdBy", "ID",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("createdBy")));
        config.Fields.Add(CollectionSchemaBuilder.Field("snapshot", "Any",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("snapshot")));
        return ObjectType.CreateUnsafe(config);
    }

    internal static ObjectFieldConfiguration RevisionsField(string collection)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.RevisionsFieldName(collection), null,
            TypeReference.Parse("[Revision!]!"),
            resolver: ctx => ResolveList(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        return config;
    }

    internal static ObjectFieldConfiguration RevisionField(string collection)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.RevisionFieldName(collection), null,
            TypeReference.Parse("Revision"),
            resolver: ctx => ResolveSingle(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        config.Arguments.Add(new ArgumentConfiguration("revisionNumber", null, TypeReference.Parse("Int!")));
        return config;
    }

    private static async ValueTask<object?> ResolveList(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var list = await ctx.Service<IGraphQlDataSource>().ListRevisionsAsync(collection, id, ctx.RequestAborted);
        return list.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["revisionNumber"] = r.RevisionNumber,
            ["operation"] = r.Operation,
            ["createdAt"] = r.CreatedAt,
            ["createdBy"] = r.CreatedBy,
            ["snapshot"] = null,                 // list = metadata only
        }).ToList();
    }

    private static async ValueTask<object?> ResolveSingle(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var n = ctx.ArgumentValue<long>("revisionNumber");
        var rec = await ctx.Service<IGraphQlDataSource>().GetRevisionAsync(collection, id, n, ctx.RequestAborted);
        if (rec is null) return null;
        object? snapshot;
        try { snapshot = JsonSerializer.Deserialize<JsonElement>(rec.Snapshot); }
        catch (JsonException) { snapshot = rec.Snapshot; }
        return new Dictionary<string, object?>
        {
            ["revisionNumber"] = rec.RevisionNumber,
            ["operation"] = rec.Operation,
            ["createdAt"] = rec.CreatedAt,
            ["createdBy"] = rec.CreatedBy,
            ["snapshot"] = snapshot,
        };
    }
}
```

> Confirm `CollectionSchemaBuilder.Field(name, sdl, resolver)` is the same helper `StruoTypeModule.TranslationType()` uses (it is — see `TranslationType`). `ctx.ArgumentValue<long>("revisionNumber")` binds the `Int!` arg; if HotChocolate hands back `int`, change the local to `int` and widen at the call (`(long)n`).

- [ ] **Step 6: Add the `revertX` mutation**

```csharp
// src/Struo.Api/GraphQl/MutationResolvers.cs — add:
internal static ObjectFieldConfiguration RevertField(string collection)
{
    var config = new ObjectFieldConfiguration(
        SchemaTypeMapper.RevertFieldName(collection), null,
        TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
        resolver: ctx => ResolveRevert(ctx, collection));
    config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
    config.Arguments.Add(new ArgumentConfiguration("revisionNumber", null, TypeReference.Parse("Int!")));
    return config;
}

private static async ValueTask<object?> ResolveRevert(IResolverContext ctx, string collection)
{
    var id = ctx.ArgumentValue<string>("id");
    var n = ctx.ArgumentValue<long>("revisionNumber");
    var reverted = await ctx.Service<IGraphQlDataSource>().RevertAsync(collection, id, n, ctx.RequestAborted);
    return reverted;   // null -> GraphQL null (REST 404 parity); shape already matches a query node
}
```

- [ ] **Step 7: Register the type + fields (revisioned collections only)**

```csharp
// src/Struo.Api/GraphQl/StruoTypeModule.cs
// In CreateTypesAsync, with the other shared value types (after DeletedFilterEnumType()):
types.Add(RevisionResolvers.RevisionType());

// In BuildQueryExtension — for each collection that keeps revisions, add the two query fields.
// Change the loop to consult metadata:
private ObjectTypeExtension BuildQueryExtension(IEnumerable<string> collectionNames)
{
    var config = new ObjectTypeConfiguration("Query");
    foreach (var name in collectionNames)
    {
        config.Fields.Add(CollectionResolvers.SingleField(name, metadata));
        config.Fields.Add(CollectionResolvers.ListField(name, metadata));
        if (metadata.GetCollection(name)?.Revisions == true)
        {
            config.Fields.Add(RevisionResolvers.RevisionsField(name));
            config.Fields.Add(RevisionResolvers.RevisionField(name));
        }
    }
    return ObjectTypeExtension.CreateUnsafe(config);
}

// In BuildMutationExtension — add revertX for revisioned collections:
private ObjectTypeExtension BuildMutationExtension(IEnumerable<string> collectionNames)
{
    var config = new ObjectTypeConfiguration("Mutation");
    foreach (var name in collectionNames)
    {
        config.Fields.Add(MutationResolvers.CreateField(name, metadata));
        config.Fields.Add(MutationResolvers.UpdateField(name, metadata));
        config.Fields.Add(MutationResolvers.DeleteField(name));
        config.Fields.Add(MutationResolvers.RestoreField(name));
        if (metadata.GetCollection(name)?.Revisions == true)
            config.Fields.Add(MutationResolvers.RevertField(name));
    }
    return ObjectTypeExtension.CreateUnsafe(config);
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~GraphQlRevisionTests"`
Expected: PASS.

- [ ] **Step 9: Full build + suite (schema still valid)**

Run: `dotnet build -warnaserror && dotnet test tests/Struo.Tests`
Expected: 0 warnings; all green (635 baseline + all new). If a committed GraphQL schema snapshot test exists (`GraphQlEndpointSmokeTests`/schema-snapshot), update it to include `Revision`, `articleRevisions`/`articleRevision`, and `revertArticle`.

- [ ] **Step 10: Commit**

```bash
git add src/Struo.Api/GraphQl/ tests/Struo.Tests/GraphQl/
git commit -m "feat(graphql): Revision type + xRevisions/xRevision + revertX parity (9c)"
```

---

### Task 9: Migration + live gate + roadmap

**Files:**
- Create: `db/migrations/006-revisions-table.sql`
- Modify: `docs/ROADMAP.md`

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: Write the migration**

```sql
-- db/migrations/006-revisions-table.sql
-- Phase 9c — the framework `revisions` table (per-item snapshot history).
--
-- CONTEXT: SqlSugar `InitTables` creates missing TABLES on a freshly provisioned (dev) database, but
-- live databases provisioned before this merge need the table created here.
--
-- IDENTIFIER NAMING — lowercase, unquoted (SqlSugar emits unquoted identifiers; Postgres folds to
-- lowercase), matching how the CLR properties map (e.g. `CollectionName` -> `collectionname`).
--
-- `snapshot` is `text` (unbounded) — a realistic snapshot overflows varchar. Idempotent (safe to re-run).

CREATE TABLE IF NOT EXISTS revisions (
    id             uuid        NOT NULL PRIMARY KEY,
    collectionname varchar(255) NOT NULL,
    itemid         varchar(255) NOT NULL,
    revisionnumber bigint      NOT NULL,
    operation      varchar(255) NOT NULL,
    snapshot       text        NOT NULL,
    createdat      timestamp   NOT NULL,
    createdby      uuid        NULL
);

-- Fast lookup of an item's revisions (list newest-first) + the max()+1 sequence in CaptureAsync.
CREATE INDEX IF NOT EXISTS ix_revisions_item ON revisions (collectionname, itemid, revisionnumber);
```

> Before applying, confirm the physical column names SqlSugar CodeFirst emits for `Revision` on Postgres against a dev-provisioned table (`\d revisions`) and match the DDL to them (the same lowercase-unquoted convention the audit/soft-delete columns follow). Adjust identifier names if they differ.

- [ ] **Step 2: Apply to the live DB + run the live gate (real Postgres)**

Apply `006-revisions-table.sql` to `web-struo-cms-db`, run the dev API against live PG + Redis, and verify (send UTF-8 via PowerShell `Invoke-RestMethod` or a UTF-8 file — per the live-verify note; set `ASPNETCORE_URLS=:5080` if using the Vite proxy convention):

- create `article` (en + zh-TW, with `category` + `tags` + `regions`) → `GET .../revisions` shows **1** (`create`); update `status` → **2** (`update`, newest-first).
- `GET .../revisions/1` → snapshot is structured JSON; **CJK code-point-exact** (`人工智慧` = U+4EBA U+5DE5 U+667A U+6167) inside the stored snapshot; the snapshot is **not truncated** (proves the `text` column — a large body with all field kinds round-trips).
- `POST .../revisions/1/revert` → item's `status` returns to the rev-1 value; `category`/`tags`/`en`+`zh-TW` translations all restored; `GET .../revisions` now shows **3** (newest `revert`); forward history (rev 2) still present (**append-only**).
- **REST ↔ GraphQL parity:** `articleRevisions(id)`, `articleRevision(id, revisionNumber)`, and `revertArticle(id, revisionNumber)` behave identically over `/graphql`; a `$variable` revert also resolves.
- Confirm **no** SQLite-green ≠ Postgres-correct surprise (capture inside the write transaction, the `text` snapshot column, and the monotonic sequence all behave on real PG).

- [ ] **Step 3: Update the roadmap**

Add a Phase 9c "done & live-verified" bullet (mirror the 9b/9a entries: what shipped, the new `dotnet test` count, the live-gate result, any fix) + set the `| 9c | Revisions | ... |` phase-table row to ✅ with the spec/plan links. Note the "next up" line: remaining Phase 9 slice **9d (lifecycle hooks)**, plus the frontend companion **9c-fe** (revision-history/revert UI).

- [ ] **Step 4: Commit**

```bash
git add db/migrations/006-revisions-table.sql docs/ROADMAP.md
git commit -m "docs(roadmap): Phase 9c revisions done + live-verified; migration 006 (9c)"
```

---

## Self-Review

**Spec coverage:**
- §1/§2/§4 attribute opt-in + `CollectionMetadata.Revisions` + scanner → Task 1. ✅
- §3 `revisions` table (text snapshot, not `[CmsCollection]`, not `IAuditable`/`ISoftDeletable`) + InitTables → Task 2. ✅
- §5.1 `IRevisionStore` port + impl (monotonic per-item number) → Task 3. ✅
- §5.2 canonical revert-capable snapshot builder (CmsField, M2O FK, M2M arrays, all-locale translations, no RBAC filter) → Task 4. ✅
- §5.3 capture inside the write transaction on create/update (rolls back with the write) → Task 5. ✅
- §5.4 revert (append-only, `revert` label, overlay translation semantics via UpdateCore) → Task 6. ✅
- §5.5 list/get revisions (read-gated) → Task 6. ✅
- §6 REST endpoints (list no-pagination, get incl. snapshot, revert) + envelope + 404 → Task 7. ✅
- §7 GraphQL parity (shared `Revision` type, `xRevisions`/`xRevision`, `revertX`, revisioned-only, `Any` snapshot) → Task 8. ✅
- §8 error handling (reused exception mapping, no new types) → implicit across Tasks 6–8. ✅
- §9 testing (unit/integration/DDL/live) → Tasks 1,2,3,4,5,6,7,8 + Task 9 live gate. ✅
- §10 migration + sample (`Article` opt-in) → Tasks 5 (sample) + 9 (migration). ✅
- §11 acceptance gate → Task 8 Step 9 + Task 9. ✅
- §12 out-of-scope (fe UI, diff, retention, delete-triggered, strict-locale revert, identity default, list pagination) → not implemented (correct). ✅

**Placeholder scan:** Test bodies in Tasks 4–8 intentionally reference the repo's standardized harnesses (`SqlSugarItemRepositoryTests`, `DeepExpansionTests`, `ItemsEndpointTests`, `GraphQlEndpointTests`, `RbacEnforcementTests`, `StructuredColumnMappingTests`) rather than inlining ~200 lines of boilerplate each, but every outline states the exact behaviour, HTTP/GraphQL calls, and assertions. All production-code steps show complete code. The two "confirm SqlSugar column names" / "confirm `ctx.ArgumentValue<long>`" notes are genuine environment/API checks with concrete fallbacks, not deferred work.

**Type consistency:** `RevisionInfo(long,string,DateTime,Guid?)` / `RevisionRecord(long,string,DateTime,Guid?,string)` used identically across store (Task 3), builder-adjacent service (Task 6), REST (Task 7), GraphQL data source + resolvers (Task 8). `IRevisionStore.CaptureAsync/ListAsync/GetAsync` signatures match between port (Task 3), impl (Task 3), and `ItemService`/builder callers (Tasks 5–6). `RevisionSnapshotBuilder.BuildAsync(string, object, CancellationToken)` consistent between Task 4 (def) and Task 5 (call). `UpdateCoreAsync(collection,id,body,operation,ct)` defined in Task 5 and called by `RevertAsync` in Task 6 with `"revert"`. `RevisionsFieldName`/`RevisionFieldName`/`RevertFieldName` (Task 8 SchemaTypeMapper) consistent with `RevisionsField`/`RevisionField`/`RevertField` usage in `StruoTypeModule` (Task 8). `ItemService` public `UpdateAsync` signature unchanged (delegates to core), so existing callers/tests are unaffected.
```
