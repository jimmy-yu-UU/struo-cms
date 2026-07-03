# Phase 7d — Relation Editing (pickers) + List Translated Columns Design

> **Status:** design (spec). Follows the phase cycle brainstorm → write-plan → execute → verify (§17.1).
> **Predecessor:** Phase 7c (item detail + create/edit/delete forms, scalar + i18n) — merged to `main`,
> live-verified at the API level. Scalar fields render via a `FieldInput` dispatcher; relations,
> files, TipTap, multi-value selects, and structured editors render as a read-only fallback.
> **Successor:** a later sub-phase for File/Image/Files upload + TipTap, then multi-value selects +
> structured editors (`Json`/`KeyValue`/`Repeater`). Phase 7d is sliced to **relations only**.

## §0 Goal & scope

Give the admin SPA **schema-driven relation editing** for content-to-content relations, plus fix the
collection **list** so translatable columns (e.g. Title) render instead of showing `—`, which in turn
unblocks the full create→edit→delete UI E2E that Phase 7c could not complete.

The backend write path is **already complete** for relations (verified from source): many-to-one (M2O)
relations write a scalar foreign-key property that `System.Text.Json` binds case-insensitively; M2M
relations are synced by `ItemService.SyncM2MAsync` from an id array carried under the relation's
camelCase name; `SchemaController` already returns `CollectionMetadata.Relations`; and
`ItemService.QueryAsync` already overlays a `translations` map onto every list row. **Phase 7d is a
front-end phase.** The one non-frontend change is adding an M2M relation to the sample Blog app so
`TagSelect` can be live-verified — sample/demo scaffolding, never framework code (§2 CLAUDE.md).

**In scope**
- Four relation interfaces (`Domain/Metadata/Enums/RelationInterface`):
  - `Dropdown` — M2O single-select, **editable** (writes the FK scalar).
  - `TagSelect` — M2M multi-select, **editable** (writes an id array under the relation name).
  - `TreeSelect` — self-referencing M2O, **editable** (writes the parent FK), rendered as a tree.
  - `RelatedList` — inbound one-to-many, **read-only** display of items referencing this one.
- Front-end relation types on `CollectionMeta`; a `relationInputKind` classifier; a `RelationInput`
  dispatcher; a generic lazy/searchable/paginated `RelationPicker` (single/multi/tree modes); a
  read-only paginated `RelatedList`; pure helpers (`resolveDisplayLabel`, `relationTargetQuery`,
  `buildRelationTree`); payload/inflation helpers extended for relations.
- Relations render in a **shared** "Relations" section of `ItemForm` (relations are never per-locale;
  they are not under the locale tabs).
- Collection **list**: translatable columns resolve from `row.translations[defaultLocale]`.
- Full create→edit→delete UI E2E (`relations.spec.ts`).

**Out of scope (deferred)**
- `File` / `Image` / `Files` upload and the file relation interfaces (`FilePicker`, `ImagePicker`,
  `FilesPicker`) — next sub-phase (these are file relations, not content-to-content relations).
- Real TipTap rich-text editor — still the Textarea fallback from 7c.
- Multi-value select family (`MultiSelect`, `CheckboxGroup`, `Tags`) and structured editors
  (`Json`, `KeyValue`, `Repeater`).
- `FieldInput` label a11y (a separate 7c follow-up).
- A locale **switcher** for the list — 7d resolves translatable columns using the default locale only;
  a per-list locale selector is a later phase.

Any relation interface not explicitly supported renders as a **read-only fallback** so the form never
breaks when it meets something 7d does not yet edit.

## §1 Backend & sample changes (no framework change)

**No framework/API change is required.** Confirmed from source:
- `GET /api/schema/{collection}` returns `CollectionMetadata` including `Relations[]`
  (`Name`, `Label`, `Kind`, `TargetCollection`, `Interface`, `ForeignKey`, `DisplayTemplate`,
  `PickerQuery`, `OnDelete`, `Editable`, `SelfReferencing`). No DTO change.
- M2O/`TreeSelect` write: the FK is a plain settable property (e.g. `CategoryId`, `ParentId`) with no
  `[CmsField]`, so it is **not** projected into item responses but **is** accepted by
  `ItemService.Deserialize` (case-insensitive `JsonSerializerDefaults.Web`). Sending
  `{ "categoryId": "<guid>" }` sets it; sending `null` clears it (nullable FK).
- M2M write: `ItemService.SyncM2MAsync` reads `body[relationName]` as an id array, validates each id
  exists in the target collection (else 400), and replaces the junction rows. Absent key = no-op
  (partial update). Empty array = clear all.
- Current relation values on **edit**: because the FK scalar is not projected and M2M has no scalar,
  the form obtains current selections via **deep expansion** (`GET /items/{coll}/{id}?deep=<rel,…>`),
  which nests the projected target row(s) under the relation name.
- `RelatedList` (inbound O2M): queried directly via
  `GET /items/{targetCollection}?filter[<foreignKey>][_eq]=<parentId>&…` (filter query-string format
  confirmed in `QueryParser.ParseQueryString`). The query includes `locale` so translatable target
  titles are overlaid.

**Sample change (demo data, not framework):** add a `Tag` collection and an `Article ↔ Tag` M2M
relation (`TagSelect`) to `samples/Struo.Sample.Blog`, plus seed rows, so `TagSelect` editing has a
real relation to exercise in the live gate. The sample already has `Category` (M2O `Dropdown` on
Article; self-referencing `TreeSelect` + two `RelatedList`s on Category), which cover the other three
interfaces.

## §2 Front-end architecture

Layered exactly as Phase 7a/7b/7c (`api` → `stores` → pure helpers → components → router), one
responsibility per file. The relation surface mirrors the proven 7c **classifier + dispatcher**: a
pure `relationInputKind` maps a `RelationInterface` to a rendering kind; `RelationInput.vue` dispatches
to the right control. A single generic `RelationPicker.vue` handles all editable kinds via props
(`multiple` for M2M, `tree` for TreeSelect) rather than one component per interface — less duplication,
one place to test lazy loading. The wire/query DSL stays confined to `itemsApi` + the query/payload
helpers; views/components never build raw query strings.

**New files**

| File | Responsibility |
|---|---|
| `src/lib/relationInputKind.ts` | Pure: `(interface: string) → 'dropdown' \| 'tagSelect' \| 'treeSelect' \| 'relatedList' \| 'readonly'`. Unknown → `readonly`. |
| `src/lib/resolveDisplayLabel.ts` | Pure: `(row, relation, targetMeta, locale) → string`. `DisplayTemplate` (`{Field}` interpolation) → `targetMeta.defaultDisplayField` → `id`. Translatable display fields read `row.translations[locale][field]`; else top-level. Never returns empty for a non-null row. |
| `src/lib/relationTargetQuery.ts` | Pure: builds the target-collection list params (reuses `buildListQuery`) with optional `filter` (`filter[field][_eq]=value`) and `locale`. |
| `src/lib/buildRelationTree.ts` | Pure: `(rows, idKey, parentKey, excludeId?) → TreeNode[]`. Builds a nested tree from a flat parent-FK list; when `excludeId` is set (self-referencing TreeSelect) drops that node and its descendants to prevent cycles. |
| `src/components/fields/RelationInput.vue` | Dispatcher: `props { relation: RelationMeta, modelValue, disabled, parentId? }`; editable kinds → `RelationPicker`; `relatedList` → `RelatedList`; unknown → read-only display. |
| `src/components/fields/RelationPicker.vue` | Generic lazy/searchable/paginated picker over `relation.targetCollection`. `multiple` (M2M id array) vs single (M2O/Tree FK). `tree` renders a PrimeVue `TreeSelect` using `buildRelationTree`. Resolves labels via `resolveDisplayLabel`; back-fills labels for preselected ids not on the loaded page via `itemsApi.get(target, id, {locale})`. Load/permission errors shown inline. |
| `src/components/fields/RelatedList.vue` | Read-only: paginated `itemsApi.list(targetCollection, { filter, locale })` on `foreignKey _eq parentId`; renders resolved titles; row click navigates to that item's edit route; renders nothing (with a hint) when `parentId` is absent (create). |

**Modified files**
- `src/types/schema.ts` — add
  `export type RelationMeta = { name: string; label: string; kind: string; targetCollection: string; interface: string; foreignKey?: string | null; displayTemplate?: string | null; editable: boolean; selfReferencing: boolean }`
  and `relations: RelationMeta[]` on `CollectionMeta` (default `[]`).
- `src/api/itemsApi.ts` — `get(name, id, opts?: { locale?; deep?: string[] })` appends `deep=a,b`;
  `list(name, opts)` gains `filter?: Record<string, { op: string; value: string }>` and `locale?`.
- `src/lib/buildListQuery.ts` — accept optional `filter` (emit `filter[field][_op]=value`) and `locale`.
- `src/lib/buildItemPayload.ts` — merge relation values: M2O/TreeSelect → `payload[camelCase(foreignKey)] = id | null`; M2M → `payload[relationName] = ids`. Untouched relations are omitted (partial update).
- `src/lib/parseItemToForm.ts` — seed relation current values from the deep-expanded item: M2O/Tree → `item[relationName]?.id ?? null`; M2M → `(item[relationName] ?? []).map(r => r.id)`.
- `src/components/ItemForm.vue` — add a shared "Relations" section rendering `meta.relations` via `RelationInput` (outside the locale tabs); relation values flow into the emitted payload.
- `src/views/ItemFormView.vue` — on edit, fetch with `deep = <editable relation names>` (+ `locale`) so current relation values inflate. Pass `parentId` to `RelationInput` for `RelatedList`.
- `src/views/CollectionListView.vue` — for a `translatable` column, read `row.translations[defaultLocale]?.[field]` (default locale from `languageStore`); else top-level. `formatCell` unchanged.

## §3 Interface → control mapping (7d relation set)

`relationInputKind(interface)` classifies; `RelationInput.vue` renders:

| Kind | Interface | Editable | Control | Payload |
|---|---|:---:|---|---|
| dropdown | `Dropdown` | yes | `RelationPicker` (single) → PrimeVue `Select` (lazy) | `payload[camelCase(foreignKey)] = id \| null` |
| tagSelect | `TagSelect` | yes | `RelationPicker` (multiple) → PrimeVue `MultiSelect` (lazy) | `payload[relationName] = [ids]` |
| treeSelect | `TreeSelect` | yes | `RelationPicker` (tree) → PrimeVue `TreeSelect` | `payload[camelCase(foreignKey)] = id \| null` |
| relatedList | `RelatedList` | no | `RelatedList` (paginated, read-only) | not submitted |
| readonly | anything else (incl. `FilePicker`/`ImagePicker`/`FilesPicker`, unmapped) | no | read-only display | not submitted |

A relation whose `editable === false` renders its control disabled regardless of kind.

## §4 Data flow

**Display-label resolution (`resolveDisplayLabel`, shared):**
1. `relation.displayTemplate` (e.g. `{Name}`, `{Title}`) → interpolate from the target row; a
   translatable field reads `row.translations[locale][field]`, else the top-level value.
2. No template → `targetMeta.defaultDisplayField`.
3. Neither present / empty → the row `id` (never blank for a real row).

**Create (`/collections/:name/new`)**
1. Blank relation model: M2O/Tree → `null`; M2M → `[]`. `RelatedList` sections are hidden (no parentId).
2. `RelationPicker` lazy-loads options via `relationTargetQuery` → `itemsApi.list(target, {filter, search, locale, page})`; labels via `resolveDisplayLabel`.
3. `buildItemPayload` merges relation values (see §3) → `itemsApi.create` → route to list on success.

**Edit (`/collections/:name/:id`)**
1. `ItemFormView` fetches the parent with `deep = <editable relation names>` and `locale`.
2. `parseItemToForm` reads current selections from the nested relation objects (M2O/Tree → `nested.id`; M2M → `nested[].id`).
3. `RelationPicker` back-fills labels for preselected ids not on the first page via `itemsApi.get(target, id, {locale})`.
4. Edit → `buildItemPayload` (partial) → `itemsApi.update`. An empty M2M array clears the relation
   (backend replaces junction rows).

**RelatedList (read-only inbound)**
- On mount, paginated `itemsApi.list(targetCollection, { filter: { [foreignKey]: { op: '_eq', value: parentId } }, locale, page })`; render resolved titles; row click → that item's edit route. No `deep` (may be large; paginates itself). `locale` ensures translatable titles overlay.

**TreeSelect (self-referencing M2O)**
- Single-select M2O semantics (writes the parent FK) with a tree menu. The tree is built by
  `buildRelationTree` from the target collection's rows keyed on its self-FK; the current item and its
  descendants are excluded to prevent selecting a cycle.

**List translated columns**
- `CollectionListView` resolves a `translatable` column from `row.translations[defaultLocale]?.[field]`
  (default locale from `languageStore`), else the top-level value. This removes the `—` placeholder and
  lets the E2E locate a row by its title.

## §5 Error handling

Built on 7a's `apiClient` (unwraps `{data}`, throws `error.message`, invokes the 401 handler); errors
are never silently swallowed (CLAUDE.md).

- **Target read denied (403):** `RelationPicker`/`RelatedList` show an inline "cannot load options
  (insufficient permission)" notice; the rest of the form still works; an existing selection still
  shows by id.
- **Target query / network failure:** inline error + retry inside the control; other fields stay editable.
- **Preselected value deleted (FK → missing row):** the label back-fill `get` 404s → show the raw `id`
  (or a "deleted item" marker); no exception.
- **M2M validation (backend 400):** submitting a non-existent id yields
  `One or more ids … do not exist`; surfaced in the form's error banner.
- **TreeSelect cycle:** `buildRelationTree` excludes self + descendants; a self-referencing FK that
  still reaches the backend is a data error and surfaces the backend message.
- **RelatedList on create:** no `parentId` → section hidden / "visible after saving"; no query issued.
- **Translatable display-field search limitation:** picker search uses the backend `search` param
  (searchable fields only). If a target's display field is translatable and not `Searchable`, search may
  not match it. **Known limitation**, recorded here and re-checked at the gate — not hidden.
- **Permission gating:** relation editing follows the form's `canWrite` (picker disabled when false).
  `RelatedList` is read-only regardless of write, but subject to target `canRead` (above).

## §6 Testing strategy

Failing-test-first for logic (§17.2). Pure helpers are the primary coverage; components test
interactions; one E2E covers the full CRUD flow.

**Front-end pure helpers (Vitest, red→green) — primary coverage**
- `relationInputKind.test.ts`: each interface → documented kind; unknown → `readonly`.
- `resolveDisplayLabel.test.ts`: template interpolation; translatable field from `translations[locale]`;
  fallback to `defaultDisplayField`; final fallback to `id`.
- `relationTargetQuery.test.ts`: builds `filter[field][_eq]=…`, `locale`, and pagination params.
- `buildRelationTree.test.ts`: nests by parent FK; excludes self + descendants (cycle guard).
- `buildItemPayload.test.ts` (extend): M2O/Tree write `camelFk`; M2M write the relation-name array;
  empty M2M array clears; untouched relations omitted on partial update.
- `parseItemToForm.test.ts` (extend): M2O/Tree from `nested.id`; M2M from `nested[].id`; none → empty.

**Front-end components (Vitest + Vue Test Utils)**
- `RelationPicker.test.ts`: lazy call hits the right path; single/multi v-model round-trip; preselected
  label back-fill; disabled state; inline load error.
- `RelatedList.test.ts`: filters by `foreignKey _eq parentId`; paginates; resolves titles; row-click
  navigation; no query on create (absent parentId).
- `RelationInput.test.ts`: dispatches to the correct child per kind; unknown → read-only.
- `ItemForm.test.ts` (extend): relations render in the shared section (not under locale tabs);
  relation values merge into the emitted payload.
- `ItemFormView.test.ts` (extend): edit fetches with `deep=<editable relations>` and inflates current
  values.
- `CollectionListView.test.ts` (extend): a translatable column reads `translations[locale]` and no
  longer shows `—`.

**API (Vitest)**
- `itemsApi.test.ts` (extend): `get` appends `deep`/`locale`; `list` appends `filter`/`locale` to the
  query string correctly.

**E2E (Playwright, same-origin dev-proxy path)**
- `relations.spec.ts`: login → create an `article` (default-locale title/body + status + pick a
  `category` via Dropdown + pick `tags` via TagSelect) → save → the list shows the **translated Title**
  (no `—`) → open the row → change category, add/remove a tag → save → open the `category` and see the
  article in its `Articles` RelatedList → delete the article → the list row is gone. Seeded data + admin
  creds as in the existing `e2e/README.md`; sample gains the `Tag`/M2M scaffolding (§1).

## §7 Verification gate (evidence required, §17.2)

- Backend: `dotnet build` clean (warnings-as-errors); `dotnet test` all green (regression after adding
  the sample `Tag`/M2M; any added metadata test green).
- Front-end: `pnpm test` green; `pnpm build` succeeds; `pnpm e2e` passes.
- **Live gate (real Postgres + Redis):** because this touches relation writes, M2M junction sync, i18n
  overlay, and RBAC — create an `article` with a `category` + `tags`, edit it to change the category and
  add/remove a tag, and confirm: the M2M junction rows are replaced correctly, the `category`'s
  `Articles` RelatedList returns the article, and the list's translatable Title column renders. Record
  evidence (SQLite-green ≠ Postgres-correct; watch the bigint/uuid class of bugs at the DB boundary).
- Version policy (§17.5): 7d uses existing PrimeVue controls (`Select`, `MultiSelect`, `TreeSelect`,
  `DataTable`) — **no new runtime dependency**. Any package that does prove necessary is installed via
  `pnpm add` (never a hand-authored version).

## §8 Self-review notes

- **YAGNI:** file relations, TipTap, multi-value selects, and structured editors stay deferred and
  render read-only. One generic `RelationPicker` (props-driven) instead of four components. `RelatedList`
  paginates rather than deep-expanding, avoiding an unbounded nested payload.
- **Dependency rule (§2):** no framework/API change; the sample M2M lives in `samples/*`; framework
  never references samples. The query/wire DSL stays inside `itemsApi` + query/payload helpers.
- **Ambiguities resolved:** relations are shared (never per-locale) → rendered outside locale tabs;
  current relation values obtained via `deep` expansion (FK scalar is unprojected); `RelatedList` via a
  standalone paginated filtered query (scales + overlays translatable titles); `TreeSelect` is M2O with
  a tree menu and a self/descendant cycle guard; list translatable columns resolved at the default
  locale (no list locale switcher this phase).
- **Known limitation (recorded, not hidden):** picker search covers `Searchable` fields only; a
  translatable, non-searchable display field may not be matchable by search — re-checked at the gate.
