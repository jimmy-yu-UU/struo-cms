# Audit Remediation Batch 5 — ARC-1 ItemService Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decompose the 1251-line `ItemService` god class (ARC-1) into focused collaborators — `ItemDeserializer` + per-interface `IFieldValidator` registry, `ItemProjector`, `TranslationOverlay`, `DeepExpansionCoordinator`, write-side sync and purge pipeline — converging `ItemService` to an orchestration layer under 400 lines, plus the ARC-4 tail (cached generic-dispatch delegates in `SqlSugarItemRepository`). **Zero behavior change.**

**Architecture:** Pure extract-class refactor. Every moved method is moved **verbatim** (same statements, same ordering, same exception types and byte-for-byte identical message strings). New collaborators live in `Struo.Application.Query` (subfolders `Write/`, `Projection/`, `Read/`) and are **constructed in `ItemService` field initializers from its existing primary-ctor parameters** — the ARC-4 precedent — because 16 test files construct `new ItemService(...)` directly and the acceptance gate forbids editing any existing test line.

**Tech Stack:** .NET 10 / C#, SqlSugarCore, xUnit (SQLite in tests). No new packages. No migrations.

## Global Constraints

1. **`ItemService` primary-ctor signature is FROZEN** — exactly the current 14 parameters, same order. 16 test files call `new ItemService(repository, metadata, registry, permissions, graph, expander, m2mSource, relationFilter, languages, options, sanitizer, currentUser, revisions, snapshotBuilder)` and must compile unchanged.
2. **No existing test file may be edited** — after every task, `git diff --stat -- tests/` must be empty. Adding *new* test files is allowed but not required (the existing 773 tests are the characterization harness).
3. **Zero behavior change** — includes: exception types (`QueryException`, `PermissionDeniedException`, `RelationConflictException`, `CollectionNotFoundException`), **byte-for-byte identical exception message strings** (tests assert them), the relative ordering in which validation phases run (exception precedence when a body has multiple violations), transaction boundaries, and the concrete runtime type of projected rows (`Dictionary<string, object?>` — callers downcast).
4. `IItemUseCases`, `PagedResult`, REST controllers, GraphQL adapters, frontend: **untouched**. `PagedResult` record stays declared in `ItemService.cs`.
5. All new classes: `public sealed` (or `internal static` for pure helpers), namespace **`Struo.Application.Query`** regardless of subfolder. No new DI registrations — field-initializer construction IS the wiring (documented ARC-4 precedent; `OrderByExpressionBuilder`'s existing scoped registration stays as-is).
6. Files ≤ 400 lines each for new collaborators; final `ItemService.cs` < 400 lines.
7. Domain stays package-free; dependency rule §2 unchanged (all new files are Application-layer, no new project references).
8. Line-range references below are against `src/Struo.Application/Query/ItemService.cs` at commit `3f8ed5f` (current HEAD). Re-locate by method name if drift occurs.
9. Verification command per task: `dotnet test --nologo` (full backend suite, expect **773 passed** — more only if a task adds new test files, never fewer).

**Decision recorded (deviation from Batch-4 memory note):** the "wire OrderByExpressionBuilder ctor injection" tail is **dropped as YAGNI** — Batch 5 introduces no direct consumer of the builder; rewiring it through the repository ctor would churn 18 test ctor sites for zero functional gain, contradicting the batch's tests-unchanged gate. The scoped DI registration (already present) and self-construction stay. The `MakeGenericMethod` delegate-cache tail (Task 5) IS in scope.

---

## File Structure (target state)

```
src/Struo.Application/Query/
  ItemService.cs                     (shrinks 1251 → <400; orchestration + permission gates + txn boundaries)
  Write/
    JsonBodyUtil.cs                  internal static: StripKeys, JsonValue, TryReadVersion
    RichTextCleaner.cs               public sealed (IHtmlSanitizer): Sanitize, static IsRichTextField
    FieldValueRules.cs               internal static: shared Required/MaxLength primitives (parent + locale message variants)
    IFieldValidator.cs               public interface: per-FieldInterface validate/normalize strategy
    FieldValidatorRegistry.cs        public sealed: ordered validator phases
    Validators/TagsFieldValidator.cs
    Validators/OptionMultiValueFieldValidator.cs   (MultiSelect + CheckboxGroup)
    Validators/KeyValueFieldValidator.cs
    Validators/FilesFieldValidator.cs
    Validators/RepeaterFieldValidator.cs
    ItemDeserializer.cs              public sealed: body → entity + full write-side field validation
    ItemWriteSideSync.cs             public sealed: SyncM2MAsync + SyncTranslationsAsync
    ItemPurgePipeline.cs             public sealed: CheckRestrictAsync + PurgeCoreAsync + TypedId
  Projection/
    ItemProjector.cs                 public sealed: Project + ProjectFor
  Read/
    TranslationOverlay.cs            public sealed: translations map + per-locale image resolution
    DeepExpansionCoordinator.cs      public sealed: deep-tree validation + batched expansion + nesting merge
src/Struo.Infrastructure/Query/
  SqlSugarItemRepository.cs          (Task 5 only: cached generic-dispatch delegates; ctor UNCHANGED)
```

---

### Task 1: ItemDeserializer + IFieldValidator registry (+ SyncTranslationsAsync dedup)

**Files:**
- Create: `src/Struo.Application/Query/Write/JsonBodyUtil.cs`
- Create: `src/Struo.Application/Query/Write/RichTextCleaner.cs`
- Create: `src/Struo.Application/Query/Write/FieldValueRules.cs`
- Create: `src/Struo.Application/Query/Write/IFieldValidator.cs`
- Create: `src/Struo.Application/Query/Write/FieldValidatorRegistry.cs`
- Create: `src/Struo.Application/Query/Write/Validators/TagsFieldValidator.cs`
- Create: `src/Struo.Application/Query/Write/Validators/OptionMultiValueFieldValidator.cs`
- Create: `src/Struo.Application/Query/Write/Validators/KeyValueFieldValidator.cs`
- Create: `src/Struo.Application/Query/Write/Validators/FilesFieldValidator.cs`
- Create: `src/Struo.Application/Query/Write/Validators/RepeaterFieldValidator.cs`
- Create: `src/Struo.Application/Query/Write/ItemDeserializer.cs`
- Modify: `src/Struo.Application/Query/ItemService.cs` (remove moved code; wire field initializers; dedup `SyncTranslationsAsync` Required/MaxLength via `FieldValueRules`)

**Interfaces:**
- Consumes: existing `IEntityRegistry` (`EntityDescriptor` with `Properties` OrdinalIgnoreCase map, `FieldToProperty`, `IdProperty`, `EntityType`), `IM2MDescriptorSource`, `IHtmlSanitizer`, `PropertyAccessorCache`, `CollectionMetadata`/`FieldMetadata`, `TagItem`, `QueryException`.
- Produces (later tasks rely on these exact signatures):
  - `internal static class JsonBodyUtil` — `internal static byte[] StripKeys(JsonElement source, IReadOnlySet<string> keysToRemove)` (verbatim ItemService.cs:905-919); `internal static object? JsonValue(JsonElement e)` (verbatim :549-557); `internal static long? TryReadVersion(JsonElement body)` (verbatim :926-936).
  - `public sealed class RichTextCleaner(IHtmlSanitizer sanitizer)` — `public string? Sanitize(string? raw)` (verbatim body of `SanitizeRichText` :565-570 + private static `IsBlankHtml` :572-582); `public static bool IsRichTextField(CollectionMetadata meta, string fieldName)` (verbatim :584-587).
  - `internal static class FieldValueRules` — `internal static bool IsMissing(object? value)` (`value is null || (value is string s && string.IsNullOrWhiteSpace(s))`); `internal static void RequireParent(string fieldName, object? value)` throws `QueryException($"Field '{fieldName}' is required.")` when missing; `internal static void RequireTranslation(string fieldName, string locale, bool present, object? value)` throws `QueryException($"Required translation field '{fieldName}' is missing for locale '{locale}'.")` when `!present || IsMissing(value)`; `internal static void CheckMaxLengthParent(string fieldName, int max, string? value)` throws `QueryException($"Field '{fieldName}' exceeds maximum length {max}.")`; `internal static void CheckMaxLengthTranslation(string fieldName, int max, string locale, string? value)` throws `QueryException($"Field '{fieldName}' exceeds maximum length {max} for locale '{locale}'.")`. **Message strings byte-for-byte as quoted.**
  - `public interface IFieldValidator { void ValidateAndNormalize(FieldMetadata field, System.Reflection.PropertyInfo pi, object entity); }` — each implementation holds ONE current per-interface loop body verbatim (reads current value via `pi.GetValue(entity)`, throws the current `QueryException`s, writes the normalized value back via `pi.SetValue(entity, ...)`).
  - `public sealed class FieldValidatorRegistry` — exposes `public IReadOnlyList<(IReadOnlySet<FieldInterface> Interfaces, IReadOnlyDictionary<FieldInterface, IFieldValidator> Validators)> Phases` (or an equivalent ordered structure) encoding the **exact current phase order**: phase 1 = {MultiSelect, CheckboxGroup, Tags} (one pass over `meta.Fields`, Tags dispatches to `TagsFieldValidator`, the other two to `OptionMultiValueFieldValidator` — mirrors the single loop at :1056-1096), phase 2 = {KeyValue}, phase 3 = {Files}, phase 4 = {Repeater}.
  - `public sealed class ItemDeserializer(IEntityRegistry registry, IM2MDescriptorSource m2mSource, RichTextCleaner richText)` — `public object Deserialize(string collection, JsonElement body, CollectionMetadata meta)`.

**Requirements (verbatim-move contract):**

1. `ItemDeserializer.Deserialize` reproduces ItemService.cs:948-1201 with the **same phase sequence**:
   1. strip M2M keys + `translations` + Json-field keys, bind JSON → entity (:950-996, incl. both `JsonException → QueryException("Request body could not be parsed.")` catch paths)
   2. set Json-field raw text (:998-1011)
   3. null system/read-only fields (:1013-1021)
   4. sanitize non-translatable RichText via `richText.Sanitize` (:1023-1032)
   5. Required for non-translatable fields (:1034-1042) — now via `FieldValueRules.RequireParent`
   6. MaxLength for non-translatable fields (:1044-1051) — now via `FieldValueRules.CheckMaxLengthParent`
   7. registry phases 1–4 (:1053-1199) — each phase iterates `meta.Fields` in declaration order, skips `Translatable`, resolves `pi` exactly as today (`d.FieldToProperty.TryGetValue` → property lookup; `pi is not { CanWrite: true }` skip), then dispatches to the validator.
2. **CS-4 write-path accessor cache:** inside `ItemDeserializer`, replace every `d.EntityType.GetProperty(prop)` with `d.Properties.GetValueOrDefault(prop)` — `prop` is always an exact CLR name from `FieldToProperty`, and `Properties` is keyed OrdinalIgnoreCase, so resolution is identical (same justification as the existing comment at :1209-1211). Repeater's per-row sub-field reads keep using `PropertyAccessorCache.Read` (already cached).
3. `ItemService` changes in this task ONLY:
   - add `using` for nothing new (same namespace); add fields:
     ```csharp
     private readonly RichTextCleaner richText = new(sanitizer);
     private readonly ItemDeserializer deserializer = new(registry, m2mSource, new(sanitizer));
     ```
   - `CreateAsync`/`UpdateCoreAsync` call `deserializer.Deserialize(collection, body, meta)` instead of the private method; delete the private `Deserialize`, `SanitizeRichText`, `IsBlankHtml`, `IsRichTextField`, `JsonValue`, `StripKeys`, `TryReadVersion`, and the `MultiValueInterfaces` set + `JsonOpts` field (move `JsonOpts` into `ItemDeserializer`).
   - `RevertAsync` calls `JsonBodyUtil.StripKeys(...)`; `UpdateCoreAsync` calls `JsonBodyUtil.TryReadVersion(body)`.
   - `SyncTranslationsAsync` (stays in `ItemService` until Task 4): replace the inline required loop (:509-515) with `FieldValueRules.RequireTranslation(rf, locale, has, v)` and the inline max-length loop (:518-523) with `FieldValueRules.CheckMaxLengthTranslation(...)`; replace `SanitizeRichText(s)` with `richText.Sanitize(s)`, `IsRichTextField(meta, ...)` with `RichTextCleaner.IsRichTextField(meta, ...)`, `JsonValue(...)` with `JsonBodyUtil.JsonValue(...)`.
4. Every moved comment block moves with its code (they document load-bearing gotchas: DB-9 dedup, ArrayPool `using`, blank-row detection, etc.).

- [ ] **Step 1:** Create the 11 new files exactly as specified above (verbatim bodies from the quoted line ranges).
- [ ] **Step 2:** Rewire `ItemService.cs` as specified; delete moved members.
- [ ] **Step 3:** Run `dotnet build --nologo` — expect 0 errors/warnings-as-before.
- [ ] **Step 4:** Run `dotnet test --nologo` — expect **773 passed, 0 failed**.
- [ ] **Step 5:** Run `git diff --stat -- tests/` — expect empty output.
- [ ] **Step 6:** Commit: `refactor(application): extract ItemDeserializer + IFieldValidator registry from ItemService (ARC-1 step 1)`

### Task 2: ItemProjector

**Files:**
- Create: `src/Struo.Application/Query/Projection/ItemProjector.cs`
- Modify: `src/Struo.Application/Query/ItemService.cs`

**Interfaces:**
- Produces: `public sealed class ItemProjector(IEntityRegistry registry, IPermissionService permissions, IMetadataProvider metadata)` —
  - `public IReadOnlyDictionary<string, object?> Project(object entity, CollectionMetadata meta, IReadOnlyList<string>? fields)` — verbatim ItemService.cs:1203-1250 (**must keep returning a concrete `Dictionary<string, object?>`** — `GetAsync`, `TranslationOverlay` and `DeepExpansionCoordinator` downcast).
  - `public IReadOnlyDictionary<string, object?> ProjectFor(string collectionName, object entity, IReadOnlyList<string>? fields)` — verbatim :301-303, with a private `Meta(string)` helper (verbatim :921-922, throws `CollectionNotFoundException`).

**Requirements:**
1. `ItemService` adds field `private readonly ItemProjector projector = new(registry, permissions, metadata);`, replaces all `Project(...)` / `ProjectFor` call sites (`QueryAsync`, `GetAsync`, `CreateAsync`, `UpdateCoreAsync`, `RestoreAsync`, the `expander.ExpandAsync(..., ProjectFor, ...)` delegate argument, and `OverlayTranslationsAsync`'s file projection) with `projector.Project` / `projector.ProjectFor`; deletes the private `Project` and `ProjectFor`.
2. **CS-4 write-path accessor swaps in `ItemService`** (same identical-resolution justification): `CreateAsync` :329 and `UpdateCoreAsync` :406 `d.EntityType.GetProperty(d.IdProperty)!.GetValue(...)` → `d.Properties.GetValueOrDefault(d.IdProperty)!.GetValue(...)`; `UpdateCoreAsync` field-overlay :368 `d.EntityType.GetProperty(prop)` → `d.Properties.GetValueOrDefault(prop)`; FK-overlay :386-388 `d.EntityType.GetProperty(rel.ForeignKey, ...IgnoreCase)` → `d.Properties.GetValueOrDefault(rel.ForeignKey)` (Properties is OrdinalIgnoreCase-keyed — same PropertyInfo). `PurgeCoreAsync`/`TypedId` reflection is Task 4's move; leave it untouched here.

- [ ] **Step 1:** Create `ItemProjector.cs`; rewire + delete in `ItemService.cs`; apply the accessor swaps.
- [ ] **Step 2:** `dotnet test --nologo` — expect 773 passed. `git diff --stat -- tests/` — empty.
- [ ] **Step 3:** Commit: `refactor(application): extract ItemProjector; cached-accessor swaps on write path (ARC-1 step 2, CS-4 tail)`

### Task 3: TranslationOverlay + DeepExpansionCoordinator

**Files:**
- Create: `src/Struo.Application/Query/Read/TranslationOverlay.cs`
- Create: `src/Struo.Application/Query/Read/DeepExpansionCoordinator.cs`
- Modify: `src/Struo.Application/Query/ItemService.cs`

**Interfaces:**
- Produces:
  - `public sealed class TranslationOverlay(IItemRepository repository, IEntityRegistry registry, ItemProjector projector)` — `public Task ApplyAsync(CollectionMetadata meta, IReadOnlyList<object> entities, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string? locale, CancellationToken ct)` — verbatim ItemService.cs:108-209 (`ProjectFor("file", f, null)` → `projector.ProjectFor(...)`; `ReadProp` → `PropertyAccessorCache.Read`).
  - `public sealed class DeepExpansionCoordinator(StruoQueryOptions options, IRelationshipGraph graph, IMetadataProvider metadata, IEntityRegistry registry, IRelationExpander expander, ItemProjector projector)` — `public Task ExpandAsync(string collection, DeepSpec? deep, IReadOnlyList<object> entities, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string? locale, CancellationToken ct)` — verbatim :217-247 including the nested-merge loop, plus private `ValidateDeepTree` verbatim :256-298 and a private `Meta` helper (throws `CollectionNotFoundException`). The `ProjectFor` delegate passed to `expander.ExpandAsync` becomes `projector.ProjectFor`; `ReadProp` becomes `PropertyAccessorCache.Read`.

**Requirements:**
1. `ItemService` adds fields (field initializers may reference primary-ctor params but NOT other fields — construct a fresh projector-dependent graph carefully; the legal pattern is):
   ```csharp
   private readonly ItemProjector projector = new(registry, permissions, metadata);
   private readonly TranslationOverlay overlay;
   private readonly DeepExpansionCoordinator deepExpansion;
   ```
   with a small explicit constructor body being ILLEGAL under primary-ctor syntax — so instead initialize via field initializers that construct their OWN projector instances:
   ```csharp
   private readonly TranslationOverlay overlay = new(repository, registry, new(registry, permissions, metadata));
   private readonly DeepExpansionCoordinator deepExpansion = new(options, graph, metadata, registry, expander, new(registry, permissions, metadata));
   ```
   (`ItemProjector` is stateless; three instances are equivalent. Keep the standalone `projector` field for `ItemService`'s own call sites.)
2. `QueryAsync`/`GetAsync` call `deepExpansion.ExpandAsync(...)` and `overlay.ApplyAsync(...)`; delete private `OverlayTranslationsAsync`, `ExpandDeepAsync`, `ValidateDeepTree`, and — if now unused in `ItemService` — the static `ReadProp` wrapper.
3. Validation ordering preserved: `ValidateDeepTree` still runs **before** the `entities.Count == 0` early-return (depth/unknown-relation must reject even on zero rows — comment moves too).

- [ ] **Step 1:** Create both classes; rewire + delete in `ItemService.cs`.
- [ ] **Step 2:** `dotnet test --nologo` — 773 passed. `git diff --stat -- tests/` — empty.
- [ ] **Step 3:** Commit: `refactor(application): extract TranslationOverlay + DeepExpansionCoordinator (ARC-1 step 3)`

### Task 4: Write-side sync + purge pipeline; ItemService converges <400 lines

**Files:**
- Create: `src/Struo.Application/Query/Write/ItemWriteSideSync.cs`
- Create: `src/Struo.Application/Query/Write/ItemPurgePipeline.cs`
- Modify: `src/Struo.Application/Query/ItemService.cs`

**Interfaces:**
- Produces:
  - `public sealed class ItemWriteSideSync(IItemRepository repository, IM2MDescriptorSource m2mSource, ILanguageProvider languages, RichTextCleaner richText)` —
    - `public Task SyncM2MAsync(string collection, JsonElement body, object parentId, bool includeDeleted, CancellationToken ct)` — verbatim ItemService.cs:596-642.
    - `public Task SyncTranslationsAsync(CollectionMetadata meta, JsonElement body, object parentId, bool isCreate, CancellationToken ct)` — verbatim :457-546 as amended by Task 1 (uses `FieldValueRules` + `richText.Sanitize` + `JsonBodyUtil.JsonValue`).
  - `public sealed class ItemPurgePipeline(IItemRepository repository, IMetadataProvider metadata, IEntityRegistry registry, IRelationshipGraph graph, IM2MDescriptorSource m2mSource, IRevisionStore revisions)` —
    - `public Task CheckRestrictAsync(string collection, string id, CancellationToken ct)` — verbatim :704-720.
    - `public Task<bool> PurgeCoreAsync(string collection, string id, HashSet<(string Collection, string Id)> visited, CancellationToken ct)` — verbatim :734-777, private `Meta` helper.
    - `public object TypedId(string collection, string id)` — verbatim :780-788.

**Requirements:**
1. `ItemService` adds field initializers:
   ```csharp
   private readonly ItemWriteSideSync writeSync = new(repository, m2mSource, languages, new(sanitizer));
   private readonly ItemPurgePipeline purge = new(repository, metadata, registry, graph, m2mSource, revisions);
   ```
   `CreateAsync`/`UpdateCoreAsync` call `writeSync.SyncM2MAsync` / `writeSync.SyncTranslationsAsync` **inside the same `repository.InTransactionAsync` blocks** (transaction boundaries unchanged). `DeleteAsync` trash-branch calls `purge.CheckRestrictAsync`; the purge branch calls `purge.PurgeCoreAsync` inside its existing transaction. Delete the moved private members (`SyncTranslationsAsync`, `SyncM2MAsync`, `CheckRestrictAsync`, `PurgeCoreAsync`, `TypedId`) and, if the `richText` field is now unused in `ItemService`, delete it.
2. `ItemService` retains: ctor + fields, `QueryAsync`, `GetAsync`, `ValidateLocale`, `CreateAsync`, `UpdateAsync`/`UpdateCoreAsync`, `InvalidateLanguagesIfNeeded`, `ValidateLanguageCodeIfNeeded`, `DeleteAsync`, `RestoreAsync`, `CaptureRevisionAsync`, `RevertAsync`, `ListRevisionsAsync`, `GetRevisionAsync`, `Meta`, `RequireSuperAdminForAdminOnly`, and the `PagedResult` record.
3. Verify `wc -l src/Struo.Application/Query/ItemService.cs` **< 400**.

- [ ] **Step 1:** Create both classes; rewire + delete in `ItemService.cs`.
- [ ] **Step 2:** `dotnet test --nologo` — 773 passed. `git diff --stat -- tests/` — empty. `wc -l` < 400.
- [ ] **Step 3:** Commit: `refactor(application): extract ItemWriteSideSync + ItemPurgePipeline; ItemService converges to orchestration <400 lines (ARC-1 step 4)`

### Task 5: SqlSugarItemRepository cached generic-dispatch delegates (ARC-4 tail)

**Files:**
- Modify: `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` (ctor signature UNCHANGED)

**Requirements:**
1. For each of the ~14 dispatch sites currently doing `SomeDef.MakeGenericMethod(d.EntityType)` + `method.Invoke(this, [...])`, add a per-dispatcher `private static readonly ConcurrentDictionary<Type, TDelegateType>` cache and replace the call with a cached **open-instance delegate** invocation. Pattern (adapt the delegate type to each private helper's exact parameter list and its known `Task<T>` return type):
   ```csharp
   private static readonly ConcurrentDictionary<Type,
       Func<SqlSugarItemRepository, object, DeletedFilter, CancellationToken, Task<object?>>> GetByIdInvokers = new();
   // at the dispatch site:
   var invoke = GetByIdInvokers.GetOrAdd(d.EntityType, static t =>
       GetByIdGenericAsyncDef.MakeGenericMethod(t)
           .CreateDelegate<Func<SqlSugarItemRepository, object, DeletedFilter, CancellationToken, Task<object?>>>());
   return await invoke(this, typedId, deleted, ct);
   ```
   The existing `*Def` `MethodInfo` fields stay (they seed the delegate creation). Read each private generic helper's actual signature/return type first — the delegate types must match exactly or `CreateDelegate` throws at first use.
2. Zero behavior change; no test edits. The existing repository test suite (SQLite) plus the ItemService suites exercise every dispatcher.

- [ ] **Step 1:** Implement the delegate caches for all dispatch sites.
- [ ] **Step 2:** `dotnet test --nologo` — 773 passed. `git diff --stat -- tests/` — empty.
- [ ] **Step 3:** Commit: `perf(infrastructure): cache open-instance delegates for generic dispatch in SqlSugarItemRepository (ARC-4 tail)`

---

## Batch Gate (controller runs after all tasks + final review)

1. Full backend suite green (≥773), `git diff --stat 3f8ed5f..HEAD -- tests/` empty (or additive new files only).
2. `wc -l src/Struo.Application/Query/ItemService.cs` < 400.
3. Live quick regression on real PG (docker `postgresql-db-1`, `ASPNETCORE_URLS=:5080`, bootstrap admin `admin@admin.com`): one round each of CRUD (create/update/delete article incl. 400 validation message), i18n (translations create + locale overlay read), relations (M2M tags sync + deep expansion read), revisions (list + revert), soft-delete (trash/restore). Expect identical envelopes/status codes to Batch-4 behavior.
4. Annotate `docs/architecture-audit-2026-07-15.md` (ARC-1 ✅, ARC-4 tail ✅, CS-4 write-path ✅ + the OrderByExpressionBuilder-rewire YAGNI decision), merge `audit-batch5` → `main`, push, update memory.

## Execution Model (user directive 2026-07-16)

Planning = fable (this session). **Implementer subagents = opus. Task reviewers + final whole-branch reviewer = opus.** Subagent-driven development, one implementer at a time, task review after each, final review at the end.
