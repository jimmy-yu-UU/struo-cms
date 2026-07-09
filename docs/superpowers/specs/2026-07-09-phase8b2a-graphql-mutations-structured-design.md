# Phase 8b.2a — GraphQL mutations (structured, non-i18n) (design)

**Date:** 2026-07-09
**Status:** approved (brainstorm); pending implementation plan
**Scope:** the second mutation slice — typed GraphQL **input** for the non-i18n structured/
multi-value field kinds that Phase 8b.1 deferred, layered on the 8b.1 write backbone. 8b.2a covers
**M2M relations, File/Image scalar own-fields, Files, MultiSelect/CheckboxGroup, Tags, Json,
KeyValue, and Repeater** inputs end to end. Translatable own-fields, the typed `translations`
input, and translatable File/Image (per-locale OG image) are deferred to **8b.2b**. Advanced read
querying (cross-relation / deep filtering / multi-level nesting) is a separate read-side slice
(**8c**) and is not part of Phase 8b at all. This slice adds to the `Struo.Api/GraphQl/` layer only —
Domain / Application / Infrastructure are **untouched** and gain **no new packages** (dependency
rule §2). **`ItemService` write logic is unchanged** — all validation is reused.

---

## 1. Goal & positioning

Phase 8b.1 proved the write pipeline end to end: `createX` / `updateX` / `deleteX` per collection,
with the resolver converting a typed input to a `JsonElement` and delegating to the existing
`ItemService.CreateAsync` / `UpdateAsync` / `DeleteAsync`. It deliberately fenced the input to
**writable scalar own-fields + M2O foreign keys + optimistic `version`**; every other field kind was
omitted from the generated input (still writable via REST, just not yet typed in GraphQL).

8b.2a extends the same promise to the deferred **non-i18n** kinds: adding a `[CmsCollection]` that
uses Tags, Repeater, M2M, etc. should also yield typed create/update inputs for those fields, with
no per-collection GraphQL code. The write pipeline already validates all of them (option membership,
required-ness, MaxLength, blank-row dropping, de-duplication, M2M id existence, RichText sanitize) —
this slice is a **typed presentation layer over that pipeline**, not new write logic.

### 1.1 Decisions (locked in the brainstorm)

- **Slicing: structured non-i18n first (8b.2a), translations second (8b.2b).** The kinds in 8b.2a all
  funnel through the already-proven `dict → SerializeToElement → JsonElement → ItemService` path with
  no shape transformation in the mapper. `translations` is the only kind that needs a mapper-level
  shape transform (GraphQL list-of-entries → locale-keyed object) and carries the highest risk, so it
  gets its own slice + its own live gate.
- **Input typing: fully-typed per-collection input** (continues the 8b.1 decision; rejects a generic
  `JSON`-scalar passthrough). Each deferred kind gets an idiomatic typed SDL:

  | Field kind | Input SDL | Notes |
  |---|---|---|
  | M2M relation | `relationName: [ID!]` | target-id array; full-replace semantics |
  | File / Image (scalar `Guid?` own-field, non-translatable) | `xxxId: ID` | e.g. `heroImageId` |
  | Files (`List<Guid>`) | `[ID!]` | ordered gallery |
  | MultiSelect / CheckboxGroup (`List<string>`) | `[String!]` | option-bound |
  | Tags (`List<TagItem>`) | `[TagItemInput!]` | mirrors read-side `TagItem` |
  | Json / KeyValue | `Any` | symmetric with the read side |
  | Repeater (`List<TChild>`) | `[XFieldItemInput!]` | mirrors read-side `XFieldItem` |

- **Json / KeyValue use the `Any` scalar** (already registered for reads via `AddType<AnyType>()` +
  `AddJsonTypeConverter()`). `Json` is arbitrary JSON (inherently unstructured); `KeyValue` is a
  string→string map (GraphQL has no native map type). `Any` is symmetric with how the read side
  projects both, and the mapper serializes the coerced CLR object graph straight through. `ItemService`
  keeps its existing validation (blank KeyValue key → `BAD_USER_INPUT`; a non-string map value →
  `BAD_USER_INPUT` via the deserialize `JsonException` guard).

### 1.2 Non-goals (this slice)

- **Translatable own-fields + typed `translations` input + translatable File/Image (per-locale OG
  image)** — all deferred to **8b.2b**. The `AddWritableFields` `if (f.Translatable) continue;` guard
  stays, so no translatable field appears in a 8b.2a input.
- **Advanced read querying** (cross-relation filtering, nested filter/sort/pagination, multi-level
  nesting) — deferred to **8c**, unrelated to mutations.
- **`payload`-wrapped user errors** — errors stay top-level via the existing `StruoErrorFilter`.
- **Bulk mutations / optimistic delete** — unchanged from 8b.1.

## 2. Layer & wiring

Same layer as 8b.1 — `Struo.Api/GraphQl/`. Changed files:

```
src/Struo.Api/GraphQl/
  SchemaTypeMapper.cs          // + WritableInputSdl (extends the writable subset); input type-name helpers
  CollectionSchemaBuilder.cs   // AddWritableFields: emit deferred-kind inputs + M2M; build XFieldItemInput
  StruoTypeModule.cs           // + shared TagItemInput value type
  MutationResolvers.cs         // SentFieldsOnly -> recursive (prune nested input objects/lists)
```

No change to `MutationInputMapper.cs` (it already serializes an arbitrary dict/list/`Any` graph to a
`JsonElement`), `GraphQlDataSource.cs`, `Program.cs`, or any file outside `Struo.Api/GraphQl/`. No DI,
package, or DDL change. `AnyType` is already registered.

## 3. Input type generation & field mapping

### 3.1 Shared value type (whole schema, one copy)

- **`TagItemInput`** — `{ value: String!, label: String }`. Registered in `StruoTypeModule`
  alongside the existing `TagItem` output type. RuntimeType `IReadOnlyDictionary<string, object?>`
  (so HotChocolate coerces it to a dict the mapper serializes cleanly). `value: String!` is the one
  non-nullable input field in the whole slice — deliberate: an empty tag value is already a
  `BAD_USER_INPUT` at `ItemService`, and rejecting it in the schema is stricter-but-consistent.

### 3.2 Per-collection / per-field type (Repeater)

- **`XFieldItemInput`** (e.g. `ArticleFaqsItemInput`) — one per Repeater field, mirroring the
  read-side `BuildRepeaterItemType`: sub-fields are the lean scalar sub-field set, **all nullable**
  (per §3.4), RuntimeType `IReadOnlyDictionary<string, object?>`. Built **once per Repeater field in
  `Build`** (before the create/update inputs) and referenced by name in both `XCreateInput` and
  `XUpdateInput`; building it inside `AddWritableFields` would double-register the type (create and
  update both call `AddWritableFields`) and break the schema build. Naming:
  `SchemaTypeMapper.RepeaterItemInputTypeName(collection, field)` = read item type name + `"Input"`.

### 3.3 `SchemaTypeMapper` extension

- **`WritableInputSdl(FieldInterface, Type?)`** — the writable input subset. Covers the 8b.1 scalar
  set **plus**: `File`/`Image` → `"ID"`, `Files` → `"[ID!]"`, `MultiSelect`/`CheckboxGroup` →
  `"[String!]"`, `Json`/`KeyValue` → `"Any"`. Returns `null` for `Tags`/`Repeater` (named types the
  builder handles specially) and for excluded/translatable-handled interfaces — same "null → builder
  skips/specials" contract as the read-side `ScalarSdl`. An *included* interface with no mapping is a
  startup fail-fast (asserted by a schema-generation test), mirroring the read side.
- **Name helpers**: `TagItemInputName()`, `RepeaterItemInputTypeName(collection, field)`.

### 3.4 `AddWritableFields` extension (shared by create + update inputs)

Keeps the existing filters (`Hidden` / `ReadOnly` / `IsSystem` / **`Translatable`** skipped) and the
8b.1 "all input fields nullable, required-ness validated server-side" rule. Per own-field:

- `Tags` → `[TagItemInput!]`.
- `Repeater` (with sub-fields) → build `XFieldItemInput` (add to sink), emit `[XFieldItemInput!]`.
- everything else → `WritableInputSdl` (skip on `null`).

Relations loop:

- existing **M2O FK** → `ID` (unchanged).
- **new M2M branch** → `relationName: [ID!]` (target-id array, matching the REST body
  `{ "tags": [id, …] }` that `SyncM2MAsync` reads).

### 3.5 Mapper (no change)

HotChocolate coerces the whole input to a nested `IReadOnlyDictionary<string, object?>` /
`List<…>` / `Any` object graph. `MutationInputMapper.ToJsonElement` =
`JsonSerializer.SerializeToElement(dict, webOpts)` already produces exactly the JSON shape each
kind's `ItemService` branch expects:

- M2M / Files / MultiSelect / CheckboxGroup → JSON array of ids/strings.
- File / Image → single id string.
- Tags → array of `{value,label}` objects → `List<TagItem>` bind.
- Repeater → array of child objects → `List<TChild>` bind.
- Json → object/array/scalar; `ItemService.Deserialize` strips the key and stores `GetRawText()`.
- KeyValue → JSON object → `Dictionary<string,string>` bind.

## 4. The one new behaviour: recursive `SentFieldsOnly`

**Problem.** HotChocolate v16 backfills every declared field of a dict-runtime `InputObjectType` with
`null` when the client omits it. 8b.1's `SentFieldsOnly` prunes this at the **top level** by
intersecting the coerced dict against the argument literal's field names. Nested input objects
(`TagItemInput`, `XFieldItemInput`) are **also** dict-runtime, so their unsent sub-fields are
backfilled `null` too. Consequences:

- **Repeater**: a sub-field that is a non-nullable value type (e.g. `int`) receives `{"count":null}`,
  and STJ deserialization of the child POCO throws `JsonException` → `BAD_USER_INPUT` (400) for a
  field the client legitimately omitted.
- **Consistency**: a REST client that omits a field gets the POCO/entity default; GraphQL must match.

**Solution.** Generalise `SentFieldsOnly` to recurse, pruning against the request literal
(post-variable-substitution, so it works for inline literals *and* `$variable` forms — both tested,
as in 8b.1):

- value whose literal is an `ObjectValueNode` (nested input) → recurse; keep only sub-fields present
  in that object literal.
- value whose literal is a `ListValueNode` (e.g. `[XFieldItemInput!]`) → pair each element with its
  element literal and recurse.
- `Any` (Json/KeyValue) and scalars → pass through verbatim (`Any` is a scalar; its inner value is
  not field-backfilled and must not be pruned).

The pruned dict then goes to `MutationInputMapper.ToJsonElement`, so the serialized JSON contains
**only client-sent keys**; unsent sub-fields fall to the child POCO / entity default — exactly like
REST. Top-level behaviour is unchanged (create preserves entity defaults, update stays partial-merge).

**M2M full-replace semantics (no extra code).** `SyncM2MAsync` only syncs when the relation key is
present in the body. Top-level `SentFieldsOnly` already keeps an unsent M2M key out of the body → no
sync on a partial update (safe). An explicit empty array `[]` → junction cleared (explicit intent).
Both match REST.

## 5. RBAC / CSRF / concurrency / errors (all inherited, zero code)

Unchanged from 8b.1: `ItemService` enforces `CanWrite` + `RequireSuperAdminForAdminOnly`; optimistic
`version` on `XUpdateInput` drives the repository compare-and-swap (`ConcurrencyConflictException` →
`CONFLICT`); `POST /graphql` is behind `CsrfProtectionMiddleware`; the `StruoErrorFilter` maps
domain exceptions to stable `code`s (`FORBIDDEN` / `NOT_FOUND` / `BAD_USER_INPUT` / `CONFLICT` /
masked `INTERNAL_SERVER_ERROR`). No new error codes.

## 6. Testing strategy (TDD — failing test first, §17.2)

In the existing `tests/Struo.Tests` (SQLite for automated tests), gated finally on real Postgres.

1. **Schema generation (unit).** `XCreateInput`/`XUpdateInput` now contain each deferred kind at the
   correct SDL type (M2M → `[ID!]`, File/Image → `ID`, Files → `[ID!]`,
   MultiSelect/CheckboxGroup → `[String!]`, Tags → `[TagItemInput!]`, Json/KeyValue → `Any`,
   Repeater → `[XFieldItemInput!]`); `TagItemInput` (`value: String!`, `label: String`) and
   `XFieldItemInput` (sub-fields mirrored, all nullable) are emitted; translatable own-fields remain
   **absent** (the 8b.2b boundary holds); an unmapped included interface → startup fail-fast; SDL
   snapshot updated.
2. **Execution / integration (HotChocolate executor + SQLite).** Per kind: create + selected re-read
   round-trip (M2M ids build junctions and re-read the nested relation; Tags objects with/without
   `label`; Repeater nested round-trip + blank-row drop + sub-field required → `BAD_USER_INPUT` +
   option out-of-range → `BAD_USER_INPUT`; Json/KeyValue via `Any`; Files/File/Image ids).
   **Recursive `SentFieldsOnly` regression**: a Repeater item that sends only some sub-fields → unsent
   sub-fields fall to defaults and do **not** 400 (inline literal **and** `$variable` forms).
   Partial-merge: sending one scalar + `version` leaves unsent M2M/Repeater/multi-value fields
   untouched. M2M `[]` → junction cleared. Every error `code`.
3. **Live gate (real Postgres).** Representative mutations against live PG covering M2M, Tags (CJK
   exact by code point), Repeater (nested + option → `BAD_USER_INPUT`), Json/KeyValue, and Files
   create/update round-trip; partial-merge does not clear untouched collections; validation →
   `BAD_USER_INPUT`. Evidence recorded per the project live-gate convention (SQLite-green ≠
   Postgres-correct).

**Verification baseline continuity:** backend `dotnet build -warnaserror` clean (0 warnings) +
`dotnet test` all green (490 baseline + new 8b.2a tests). Frontend untouched (237).

## 7. Risks & open points

- **Nested null-backfill (the main new surface).** Addressed by recursive `SentFieldsOnly` (§4);
  covered by the inline-and-`$variable` Repeater regression tests and the live gate.
- **`Any` as an input scalar.** Already registered and used for reads; input coercion yields a CLR
  object graph the mapper serializes. The KeyValue "non-string value → 400" path is reused, not new.
- **Repeater sub-field SDL.** The read side maps every sub-field with `typeof(string)` as the CLR
  hint; the input mirrors this (lean scalar set). The plan pins the exact sub-field SDL mapping
  against a fixture with a numeric sub-field to confirm the recursive-prune path.
- **Deferred scope** (8b.2b translations/i18n inputs; 8c advanced read querying) is recorded here so
  it isn't lost; each is a clean follow-up on the pipeline this slice extends.
