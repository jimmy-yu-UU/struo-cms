# Phase 7g+ (slice 3) — Multi-file `Files` field (design)

**Date:** 2026-07-07
**Status:** approved (brainstorm); pending implementation plan
**Scope:** the **third 7g+ slice** — light up the `Files` field interface (multiple file references),
which has rendered read-only since 7g.6. Following the slicing discipline (7d/7e/7f/7g/7g+ slices 1–2),
this slice is the *multi-file reference* group only: `Repeater` (repeatable child objects) remains the
last deferred interface (still read-only). Like the earlier 7g+ slices this **changes persistence** (a
field now stores an ordered list of file ids as a JSON array, not a plain scalar), so it carries its
own DDL consideration and its own **live gate** on real Postgres + MinIO.

---

## 1. Goal & positioning

Phase 7e lit up the **single** file reference: `File`/`Image` are a scalar `Guid?` property storing the
selected file's id, projected as the raw id, with the frontend `FilePicker` resolving the file for
display (thumbnail + name) via `GET /api/items/file/{id}`. This slice is the **plural** of that:

- `Files` — an **ordered** list of file references (a gallery / attachment set), stored as
  `List<Guid>`.

`Files` resolves to `ReadonlyField` today. The design deliberately **mirrors the scalar `File`/`Image`
contract** (store raw ids, project raw ids, frontend resolves for display) and reuses the slice-1
multi-value JSON-column persistence convention (`List<>` via `IsJson` + `text`). The only genuinely new
surface is a reorderable multi-select picker component on the frontend.

Per the brainstorm, the field is **non-translatable, non-sortable, non-searchable**, and `MaxLength`
(7g.5) **does not apply** — the same non-goal contract as slices 1–2. **Order is meaningful and
user-arrangeable** (a gallery has an intended sequence); `List<Guid>` preserves it and the picker lets
the user drag-reorder.

## 2. Approach (decided)

**Real typed `List<Guid>` property, persisted as a JSON array in a `text` column** — the slice-1
multi-value convention applied to a list of `Guid`.

- The entity property is `List<Guid>` (natural CLR type; preserves order).
- It maps to an `IsJson` + `text` column via the existing `JsonColumnInterfaces` convention (the set
  slice 2 renamed from `MultiValueInterfaces`): the `SqlSugarClientFactory` EntityService hook sets
  `column.IsJson = true; column.DataType = "text";`. `DataType = "text"` is mandatory — `IsJson` alone
  leaves the CodeFirst length unset and Postgres makes it `varchar(1)`, truncating the payload (the
  slice-1 live-gate finding, `3b4ab40`).
- **All JSON code we author is `System.Text.Json`** (user directive: prefer .NET built-in; use
  Newtonsoft only when unavoidable). Inbound, STJ's whole-entity `Deserialize` binds the JSON array of
  guid strings straight into `List<Guid>`. Outbound, the API's STJ serializer emits the projected
  `List<Guid>` as a JSON array of guid strings. The **only** Newtonsoft touch is SqlSugarCore 5.1.4's
  internal materialization of the `IsJson` column when reading/writing the DB — the same
  framework-internal path already load-bearing for the merged slice-1 (`List<string>`) and slice-2
  (`Dictionary<string,string>`) columns. `List<Guid>` is a plain BCL type Newtonsoft round-trips
  cleanly (guids as strings); it is a value-type list, so the slice-2 `JsonElement?`-reads-back-disposed
  problem does **not** apply here.
- Satisfies CLAUDE.md §17.4 (all DB access via SqlSugar ORM, zero vendor SQL).

### 2.1 Why mirror the scalar `File`/`Image` contract (not inflate to file objects)

The scalar `File`/`Image` field (Phase 7e) projects the **raw `Guid`**; the frontend `FilePicker`
resolves the file for display. `Files` follows the same contract: `Project` returns the raw
`List<Guid>` (array of id strings) and the frontend resolves each id for its thumbnail strip. Rationale:

- **Consistency** with the already-shipped single-file contract (one mental model, one resolution path).
- **YAGNI** — no server-side inflation/expansion machinery for this slice; the frontend already knows
  how to resolve a file id (it does so per-item in `FilePicker`), and can resolve a batch in **one**
  request via the existing query DSL (`filter[id][_in]=…`) rather than N round-trips.
- The per-locale image *overlay* in `ItemService` (Phase 5.6, line ~151) already lists `Files` among
  the translatable file-resolution interfaces, but that path is **translatable-only** (SEO sidecars);
  this slice's `Files` is **non-translatable**, so it does not go through the overlay and needs no
  inflation.

Rejected: projecting resolved file objects (adds server inflation coupling this slice to the expansion
system; diverges from the scalar contract) — deferred as a possible future enhancement to *both* File
and Files together, out of scope here.

## 3. Data model

| Interface | CLR property type | Column | Stored text | API shape |
|---|---|---|---|---|
| `Files` | `List<Guid>` | `IsJson` + `text`, `NOT NULL DEFAULT '[]'` | `["<guid>","<guid>",…]` (insertion/edit order) | a JSON array of id strings, order preserved |

**Empty/null representation:** absent → empty list `[]` (the property default is a non-null empty
list, like the slice-1 multi-value lists). There is no "null gallery" distinct from an empty gallery.

**Order:** the array order **is** the display order. The picker persists whatever order the user
arranges (drag-reorder); the backend does not sort.

## 4. Backend write path & scanning

- **Column-type mapping** (`SqlSugarClientFactory` EntityService hook): add `FieldInterface.Files` to
  the existing `JsonColumnInterfaces` set (the one already holding `MultiSelect`/`CheckboxGroup`/`Tags`/
  `KeyValue`). No new branch — the same `IsJson = true; DataType = "text";` applies. An explicit
  `[SugarColumn]` still wins, as everywhere.
- **Write path** (`ItemService.Deserialize`): STJ's whole-entity `Deserialize` already binds the
  incoming `["<guid>",…]` array into the `List<Guid>` property (guid strings parse to `Guid`
  natively; a non-guid string in the array makes STJ throw, which the existing
  `JsonException → QueryException` guard from slice 2 already turns into a **400**, not a 500). A new
  **`Files` normalization branch** (running alongside the slice-1 multi-value loop and the slice-2
  `KeyValue` loop; **non-translatable fields only**) then, for each `Files` field:
  - drops `Guid.Empty` entries (defensive against a blank/empty id slipping through),
  - de-duplicates keeping **first occurrence** (preserves order; matches the multi-value keep-first
    rule),
  - enforces `Required` as a **non-empty** list → else 400 `Field '{name}' is required.`,
  - writes the cleaned `List<Guid>` back to the property.
  - **No existence validation.** Like the scalar `File`/`Image` field, a referenced id is *not* checked
    against the `file` table on write — a later-deleted file simply resolves to a raw-id fallback in the
    picker (consistent with `FilePicker`'s `missingId` behaviour). This keeps the slice decoupled from
    the file repository and matches the shipped single-file contract.
- **`MaxLength` (7g.5) does NOT apply.** The 255-default is guarded by `PropertyType == typeof(string)`;
  `Files` is a `List<Guid>`, so it never receives the default and no `MaxLength` code touches it. (No
  new exclusion needed — it is not a string.)
- **Scanner** (`MetadataScanner`): `Files` fields are **not sortable** and **not searchable** (the
  query DSL does not reach inside a JSON array — out of scope). `Files` is not option-bound, so
  `[CmsOptions]` on it is not expected.
- **Projection** (`ItemService.Project`): **unchanged / generic** — the reflected `List<Guid>` is placed
  in the projection dictionary and the API's STJ serializer emits it as a JSON array of id strings. No
  special-casing (unlike slice-2 `Json`, which needed a parse step). A `Files` value is never `null`
  (empty list default), so it projects as `[]` when empty.

### 4.1 Deferred-hardening follow-up (review M4, now actioned)

The slice-2 review deferred a scanner **startup fail-fast** on `Translatable = true` for the JSON-column
interfaces (multi-value + structured), since those live on the parent entity and translating them is
out of scope — a mis-declared `[CmsField(Translatable = true, Interface = Files/…)]` would silently
misbehave. This slice **adds that fail-fast** (covering `Files` and, retroactively, the slice-1/2
interfaces): the scanner throws at startup if any `MultiSelect`/`CheckboxGroup`/`Tags`/`KeyValue`/`Files`
field is declared `Translatable`. `Json` is intentionally **excluded** from the check (it is a `string`
that *could* in principle be translatable in a later slice; it is non-translatable by convention here
but not fail-fasted, to avoid over-constraining). This closes the latent gap for the whole 7g+ family in
one place.

## 5. Frontend (on the 7g.6 registry)

The wire contract the frontend sees is "an array of file id strings for `files`".

- **Registry entry** (`lib/fieldTypes/registry.ts`): replace the `readonlyDef` entry for `files` with a
  real def.
  - `files`: `defaultValue: () => []`, `parse: (raw) => (Array.isArray(raw) ? raw.map(String) : [])`,
    `serialize`: filter blank/empty-guid strings, de-duplicate keeping first (order preserved) → a
    `string[]`. This mirrors the `optionMultiDef` serialize shape from slice 1 (dedupe keep-first) minus
    the option-membership concern.
  - `listColumn`: **not list-eligible** (`null`) — mirrors the scalar `file`/`image` defs, which are
    also not list-eligible. A gallery has no meaningful single-cell list rendering (a count would be
    low-value); the list view is unaffected.
- **Component** `FilesField.vue` (`components/fields/`), uniform contract (`field`, `modelValue`,
  `disabled` + `update:modelValue`):
  - **Model value** is `string[]` (guid strings), order-significant.
  - Holds a component-local **`FileRow[]` working array** keyed by id, resolved in **one** batch request
    on mount / when the incoming ids change: `itemsApi.list('file', { filter: { id: { _in: ids } }, … })`
    (single request, not N). Ids that resolve to no file are kept as **raw-id fallback** rows (mirrors
    `FilePicker`'s `missingId`), so a deleted file still shows and can be removed.
  - Re-sync guard (matching slice-2 `KeyValueField`/`JsonField`): re-derive the working array from
    `modelValue` only when the incoming array differs from what the working array currently serializes
    to — so the component's own emits (reorder/add/remove) don't clobber the working state.
  - **PrimeVue `OrderList`** (already available in PrimeVue 4.5 — **no new dependency**) provides
    drag-reorder + up/down controls + a11y. `v-model` binds the working array; on `@reorder` the
    component emits the new id order. A template slot renders each row as `FileThumbnail` (reusing the
    Phase 7e media component) + file name (or the raw id for a missing file) + a remove `Button`.
  - **"Select files"** button opens a `Dialog` containing `MediaGrid` (Phase 7e) in a **multi-select**
    mode: clicking a tile toggles it in/out of the selection **without closing** the dialog; a
    "Done"/close action commits. Selecting a file appends it to the end of the working array (new files
    go last, preserving existing order); already-selected ids are marked in the grid. This requires
    `MediaGrid` to support a multi-select mode (a `multiple` prop + a `selectedIds: string[]` +
    `@toggle` event) **in addition to** its existing single-select mode (`selectable` + `selectedId` +
    `@select`, used by `FilePicker`) — an additive, backward-compatible extension; `FilePicker` is
    untouched.
  - Immutable updates throughout (spread, `filter`, `map` — never in-place mutation).
- The `FieldInterface` TS union already includes `files`, so no union change; `vue-tsc` continues to
  enforce registry exhaustiveness.
- **Registry test caveat:** `registry.test.ts`'s read-only baseline currently references a
  still-deferred interface (`repeater` after slice 2 moved it off `json`). Once `files` is a real def,
  the baseline must reference `repeater` (the sole remaining `readonlyDef` interface besides
  `hidden`/`uuid`) — verify the baseline still points at a genuinely read-only interface.

## 6. Sample field (for the live gate)

Add one **non-required** field to `Article` so the live gate has a real column:

- `Gallery` — a `Files` field (`List<Guid>`, `IsJson` `text` column).

Non-required because a *required* `Files` field on `Article` would make every existing create test that
omits it fail; `Required` behaviour is covered by a dedicated test collection (`FilesThing`, the
`MvThing`/`JsonThing`/`KvThing` pattern from slices 1–2). This adds one metadata-scan assertion (the
sample collection now exposes `gallery` with the right interface/eligibility) and requires a migration
script for pre-existing live DBs.

## 7. Testing & verification

**Backend (unit + integration):**
- column DDL: a `Files` field maps to an `IsJson` `text` column (extend/sibling the
  `StructuredColumnMappingTests`); a `List<Guid>` round-trips via actual insert+read on SQLite,
  **order preserved**.
- `Files` create + round-trip through `ItemService`: a multi-id list persists and projects back as an
  array of id strings in the **same order**; an empty/absent value projects as `[]`.
- normalization: duplicate ids de-duplicated keeping first (order preserved); `Guid.Empty` entries
  dropped.
- validation: a `Files` array containing a **non-guid string** → 400 (the STJ→`QueryException` guard);
  `Required` `Files` empty/absent → 400 `Field '{name}' is required.` (dedicated `FilesThing`
  collection).
- scanner: `Files` is non-sortable/non-searchable; **`Translatable = true` on a `Files` (or
  multi-value/`KeyValue`) field fail-fasts at startup** (§4.1) — a new assertion, plus a
  characterization test that `Json` is *not* caught by the check.

**Frontend (unit + component):**
- registry dispatch: `files` resolves to the new def (not `readonlyDef`); the read-only baseline
  assertion points at a still-read-only interface (`repeater`).
- def `parse`/`serialize`: `files` coerces non-arrays to `[]`, `parse` maps entries to strings;
  `serialize` drops blank/empty entries and de-duplicates keeping first (order preserved).
- `FilesField.vue`: renders one row per resolved file (in order); a missing id renders a raw-id
  fallback row; reorder emits the new id order; remove emits the shortened array immutably; the picker
  dialog multi-selects (toggle in/out) and appends new ids at the end; honours `disabled`.
- `MediaGrid`: the new multi-select mode toggles ids and emits `@toggle` without closing; the existing
  single-select mode (`FilePicker`) is unchanged (characterization test stays green).

**Gates:** backend `dotnet build` clean (warnings-as-errors) + `dotnet test` all green (353 current +
new); frontend `pnpm test` all green (217 current + new), `pnpm vue-tsc` clean, `pnpm build` clean.

**Live gate (real Postgres + Redis + MinIO, API-level — SQLite-green ≠ Postgres-correct; UTF-8 via
PowerShell `Invoke-RestMethod` or a UTF-8 file, never Big5 curl):**
1. Upload 3 files (`POST /api/files`) → 3 published file ids `[id1, id2, id3]`.
2. Create an `article` with `translations.en.title` and `gallery = [id1, id2, id3]` → expect 201.
3. `GET` the item back: `gallery` reads back as a JSON array `[id1, id2, id3]` in the **same order**,
   read from the `text` column (not truncated → the slice-1 `varchar(1)` bug-class is not
   reintroduced).
4. Edit (PUT): reorder to `gallery = [id3, id1]` (drop id2) → expect 200; re-`GET` confirms the new
   order `[id3, id1]` and that id2 is gone.
5. `PUT` with a duplicate id (`[id1, id1, id3]`) → 200 and re-`GET` shows de-duplicated `[id1, id3]`
   (keep-first).
6. Create with a `gallery` containing a **non-guid string** → expect **400** (not a 500 — the 7g
   bug-class guard).
7. `Required` empty (dedicated `FilesThing` collection, or a temporarily-required probe) → 400.
8. Delete one referenced file (`DELETE /api/files/{id2}`) then `GET` the article and load it in the
   SPA: the picker shows the surviving files plus a raw-id fallback for the deleted one, and it can be
   removed (frontend behaviour — verified via the SPA or by confirming the id still round-trips through
   the API unchanged).

Fix any Postgres-only issue surfaced; re-run until green.

## 8. Scope boundary / non-goals

- **Only `Files`.** `Repeater` continues to render read-only (the final 7g+ slice, its own persistence
  design + live gate). `Hidden`/`Uuid` stay read-only.
- **Non-translatable.** `Files` lives on the parent entity, not the i18n sidecar; a `Translatable`
  declaration now fail-fasts at startup (§4.1).
- **No querying inside the value.** Non-sortable and non-filterable; the query DSL is not extended to
  filter/sort by gallery membership.
- **`MaxLength` is not applied.** No max file-count limit, no per-file size/type constraint at this
  layer (upload validation is Phase 5's concern, unchanged).
- **No server-side file-object inflation.** `Files` projects raw ids; the frontend resolves for display
  (mirrors scalar `File`/`Image`). Inflating both to file objects is a possible future enhancement, out
  of scope here.
- **No existence validation on write.** A referenced id is not checked against the `file` table (matches
  the scalar contract); a deleted file degrades to a raw-id fallback in the picker.
- **`RelationInterface.FilesPicker`/`FilePicker`/`ImagePicker` remain unused reserved enum members** —
  `Files` is a `FieldInterface` (a scalar-list field), not a relation, consistent with how Phase 7e
  modelled single `File`/`Image`.
- The relations system (`RelationInput`), the multi-value select interfaces (slice 1), and the
  structured editors (slice 2) are untouched — separate axes.
