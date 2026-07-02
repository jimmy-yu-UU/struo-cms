# Phase 7c — Item Detail + Create/Edit Forms (core forms) Design

> **Status:** design (spec). Follows the phase cycle brainstorm → write-plan → execute → verify (§17.1).
> **Predecessor:** Phase 7b (collection lists) — merged to `main`, live-verified. Browse-only surface:
> RBAC nav + a lazy `CollectionListView`. Its E2E noted that Article **create** is gated by the i18n
> rule ("default-locale translation required") — this phase makes that flow possible.
> **Successor:** Phase 7d (relation pickers, File/Image upload, TipTap rich text, multi-value selects).

## §0 Goal & scope

Give the admin SPA a **schema-driven create / edit / delete** surface for **scalar** fields, with
first-class **i18n** editing (per-locale tabs), built on Phase 7b's `apiClient` / `schemaStore` /
`itemsApi`. Clicking a list row opens a unified edit form; a "New" action opens a blank create form.

**In scope**
- One additive backend endpoint: `GET /api/languages` (enabled locales + which is default).
- Frontend: `apiClient` gains `put`/`delete`; `itemsApi` gains `get`/`create`/`update`/`remove`;
  a `languagesApi` + `languageStore`; pure helpers (`fieldInputKind`, `splitFields`,
  `buildItemPayload`, `parseItemToForm`, `validateItem`); a `FieldInput` dispatcher; an `ItemForm`;
  an `ItemFormView` (routes `/collections/:name/new` and `/collections/:name/:id`).
- Scalar field interfaces rendered with PrimeVue controls (§4).
- i18n editing via locale **tabs**: non-translatable fields shared (shown once); translatable fields
  edited per locale; payload carries `translations: { locale: { field: value } }`.
- Required-field client validation + faithful surfacing of server validation/permission/conflict errors.
- Delete with confirmation; write/delete gated by the caller's effective permissions.

**Out of scope (deferred to Phase 7d unless noted)**
- Relation editing (M2O dropdown, M2M tag select, relation pickers) — relations render read-only.
- File / Image / Files upload — render read-only.
- Real TipTap rich-text editor — `RichText` is edited via a **Textarea fallback** in 7c.
- Multi-value select family (`MultiSelect`, `CheckboxGroup`, `Tags`) and structured editors
  (`Json`, `KeyValue`, `Repeater`) — render read-only.
- Advanced/per-column filter UI, locale switcher for the **list**, translated columns — later phases.

Any interface not explicitly supported in §4 renders as a **read-only fallback** so the form degrades
gracefully (never breaks) when it meets a field 7c doesn't yet edit.

## §1 Backend change — expose enabled languages (additive)

New authenticated endpoint (no new Domain/Application contract; wraps the existing
`ILanguageProvider`):

```
GET /api/languages   →   { "data": [ { "code": "en", "name": "English", "isDefault": true },
                                      { "code": "zh-TW", "name": "繁體中文", "isDefault": false } ] }
```

- Source: `ILanguageProvider.Enabled()` (already returns `LanguageInfo { Code, Name, IsDefault,
  Enabled, Sort }`, ordered by `Sort`). The endpoint projects `code`/`name`/`isDefault` (camelCase,
  §1 CLAUDE.md outbound JSON). `Enabled()` already filters to enabled locales.
- **Why a dedicated endpoint (not `/api/items/language`):** the `Language` entity is a CMS collection
  guarded by RBAC; the create/edit form must know the enabled locales + default regardless of whether
  the caller holds a `language` read grant. This mirrors Phase 7b adding permissions to `/me`.
- Auth: requires an authenticated session (same posture as other admin surfaces); anonymous callers
  get 401. Content reads stay unaffected.
- Additive: no existing endpoint changes; all prior backend tests stay green (behavior re-verified on
  live Postgres+Redis at the gate — SQLite-green ≠ Postgres-correct).

## §2 Frontend architecture

Layered exactly as Phase 7a/7b (`api` → `stores` → pure helpers → components → router). One
responsibility per file (CLAUDE.md many-small-files; the query/wire DSL never leaks into views — it is
confined to `itemsApi` + the payload helpers).

The dynamic form uses a **dispatcher + pure classifier** (chosen over an inline `v-if` chain or a
schema-form library): `FieldInput.vue` maps a `FieldMeta` to a PrimeVue control; `fieldInputKind` is a
pure, unit-tested classifier. Adding relations/files/TipTap in 7d means adding renderer branches +
classifier cases, with no change to `ItemForm`/`ItemFormView`.

**New files**

| File | Responsibility |
|---|---|
| `src/api/languagesApi.ts` | `getEnabled(): Promise<LanguageInfo[]>` over `apiClient.get('/languages')`. |
| `src/stores/languageStore.ts` | Pinia: `languages: LanguageInfo[]`, `defaultCode: string`, `load()` (fetch once + cache), `loadError`. |
| `src/lib/fieldInputKind.ts` | Pure: `(interface: string) → InputKind` — `'text' \| 'textarea' \| 'number' \| 'boolean' \| 'date' \| 'datetime' \| 'time' \| 'select' \| 'radio' \| 'richtext' \| 'divider' \| 'readonly'`. |
| `src/lib/splitFields.ts` | Pure: `(meta) → { shared: FieldMeta[], translatable: FieldMeta[] }` — excludes `isSystem`; both lists ordered by `sort`. |
| `src/lib/buildItemPayload.ts` | Pure: `(meta, model, locales) → Record<string,unknown>` — shared fields at top level; translatable under `translations[locale]`; omits empty locales on create per rule below. |
| `src/lib/parseItemToForm.ts` | Pure: `(meta, item, locales) → FormModel` — inflates a fetched item (+ its `translations` map) into `{ shared: {...}, translations: { locale: {...} } }`; missing locales seeded empty. |
| `src/lib/validateItem.ts` | Pure: `(meta, model, defaultCode) → Record<string,string>` — required non-translatable fields + required translatable fields for the **default** locale; returns a field→message map (empty = valid). |
| `src/components/fields/FieldInput.vue` | Dispatcher: `props: { field: FieldMeta, modelValue, disabled }`; `v-model`; renders the PrimeVue control per `fieldInputKind`; unsupported → read-only display. |
| `src/components/ItemForm.vue` | Renders `shared` fields once + a PrimeVue `Tabs` (one tab per enabled locale) for `translatable` fields; Save/Cancel; per-field validation messages; a server-error banner. Emits `submit(payload)` / `cancel`. |
| `src/views/ItemFormView.vue` | Route component for `new` and `:id`: loads meta (`schemaStore`) + languages (`languageStore`) + (edit) the item (`itemsApi.get`); owns create/update/delete + permission gating; wires `ItemForm`. |

**Modified files**

- `src/types/schema.ts` — add `export type LanguageInfo = { code: string; name: string; isDefault: boolean }`.
  (Form logic uses the existing `FieldMeta.translatable` flag; no new relation/translation types needed
  in 7c — those arrive with 7d.)
- `src/api/apiClient.ts` — add `put<T>(path, body?)` and `delete<T>(path)` (same envelope-unwrap +
  error/401 handling as `post`; `delete` tolerates 204/empty body → `undefined`).
- `src/api/itemsApi.ts` — add `get(name, id, opts?: { locale?: string })`, `create(name, payload)`,
  `update(name, id, payload)`, `remove(name, id)`. The wire DSL stays encapsulated here.
- `src/stores/authStore.ts` — add `canWrite(collection)` / `canDelete(collection)` getters mirroring
  the existing `canRead` (super-admin short-circuit; else `permissions[name].write|delete`).
- `src/router/index.ts` — add child routes of `AppShell`: `collections/:name/new` (name
  `collection-create`) and `collections/:name/:id` (name `collection-item`), both → `ItemFormView`.
  Existing `collections/:name` (`collection-list`) and `dashboard` retained.
- `src/views/CollectionListView.vue` — row click navigates to `collection-item`; show a "New" button
  (→ `collection-create`) only when `authStore.canWrite(name)`.

## §3 Field split & wire mapping

`ItemService` merges translatable sidecar fields into the collection's `fields[]`, each flagged
`translatable = true` (verified in `MetadataScanner`); `SchemaService` strips `Hidden` fields before
they reach the client. So the form iterates `meta.fields` and splits by the `translatable` flag:

- **Shared** (`translatable === false`, `isSystem === false`): rendered once; submitted at the payload
  top level (`categoryId`, `status`, …). System fields (`id`, timestamps) are not rendered as inputs.
- **Translatable** (`translatable === true`): rendered under each locale tab; submitted under
  `translations[locale][field]`.

**Payload rules** (`buildItemPayload`)
- **Create:** always include `translations[defaultCode]` (required by backend). Include a non-default
  locale only when at least one of its translatable fields is non-empty (avoids sending empty locales).
- **Update:** send shared fields present in the model (partial/merge semantics on the backend) and
  `translations` for locales the user touched; upsert per locale server-side.
- Empty string for an optional field is sent as-is (backend treats null/empty required as a violation;
  optional empties are allowed).

**Read inflation** (`parseItemToForm`)
- Shared values come from the item's top-level keys; translatable values from `item.translations[locale]`.
- Locales with no translation row are seeded to empty objects so every tab is editable.

## §4 Interface → control mapping (7c scalar set)

`fieldInputKind(interface)` classifies; `FieldInput.vue` renders:

| Kind | Interfaces | PrimeVue control |
|---|---|---|
| text | `Text`, `Slug`, `Email`, `Url`, `Color`, `Phone`, `Password` | `InputText` (type hint by interface; `Password` → masked) |
| textarea | `Textarea`, `Markdown`, `Code` | `Textarea` |
| richtext | `RichText` | `Textarea` (**fallback**; TipTap in 7d) |
| number | `Number`, `Slider`, `Rating` | `InputNumber` |
| boolean | `Boolean`, `Checkbox` | `Checkbox` (binary) |
| date | `Date` | `DatePicker` |
| time | `Time` | `DatePicker` (`timeOnly`) |
| datetime | `DateTime` | `DatePicker` (`showTime`) |
| select | `Select` | `Select` (options from `field.options`) |
| radio | `Radio` | `RadioButton` group (options from `field.options`) |
| divider | `Divider` | section divider (no value) |
| readonly | `File`, `Image`, `Files`, `MultiSelect`, `CheckboxGroup`, `Tags`, `Json`, `KeyValue`, `Repeater`, `Uuid`, relations, anything unmapped | read-only display (edited in a later phase) |

A field whose `readOnly === true` renders its mapped control in a disabled state regardless of kind.

## §5 Data flow

**Create** (`/collections/:name/new`)
1. `ItemFormView` ensures `schemaStore.load()` + `languageStore.load()`; permission guard: no
   `canWrite` → permission card, no further work.
2. Build a blank model: `shared` keyed empty; `translations` seeded per enabled locale.
3. User fills shared fields + per-locale translatable fields across tabs.
4. `validateItem` (blocks submit on missing required non-translatable or missing default-locale
   required translatable) → `buildItemPayload` → `itemsApi.create` → on success route to
   `collection-list`.

**Edit** (`/collections/:name/:id`)
1. Load meta + languages + `itemsApi.get(name, id)` (response includes the `translations` overlay).
2. `parseItemToForm` → model. If `!canWrite`, render controls disabled and hide Save.
3. Edit → `validateItem` → `buildItemPayload` (partial) → `itemsApi.update` → success → back to list
   (or stay with a saved indicator — implementer's call, list is the default).

**Delete**
- Edit view shows a Delete button when `canDelete`. Click → PrimeVue confirm → `itemsApi.remove` →
  route to `collection-list`.

**List integration**
- Row click → `collection-item` (`:id`). "New" button (when `canWrite`) → `collection-create`.

## §6 Error handling

Built on 7a's `apiClient` (unwraps `{data}`, throws `error.message`, invokes the 401 handler). Errors
are never silently swallowed (CLAUDE.md).

- **Client validation:** `validateItem` shows per-field messages and blocks submit; no API call.
- **400 (server validation / missing default-locale translation / bad locale):** show the server
  message in the form's error banner; when the message names a field, surface it inline if parseable
  (best-effort — the banner is the guaranteed surface).
- **403 (RBAC):** the client checks `canWrite`/`canDelete` before acting (UX only); if bypassed, the
  backend 403 surfaces the same permission card/banner (backend is the final authority).
- **404 (unknown collection or item):** "not found" card, no mutation attempted.
- **409 (delete blocked by `OnDelete.Restrict`):** show the backend message ("referenced by …") in a
  banner; the item is not removed.
- **401:** existing unauthorized handler clears the store and routes to `/login`.
- **Schema/languages load failure:** the view shows an error + retry; the app does not crash.

## §7 Testing strategy

Failing-test-first for logic (§17.2). Pure helpers are the primary focus; components test interactions;
one backend test; one E2E covering the previously-blocked create flow.

**Backend (`tests/Struo.Tests`, TDD)**
- `LanguagesEndpointTests`: `GET /api/languages` returns the enabled locales including exactly one
  `isDefault: true`; anonymous → 401. Uses the existing SQLite + `WebApplicationFactory<Program>`
  pattern. Regression: prior suite stays green (additive).

**Frontend pure helpers (Vitest, red→green) — primary coverage**
- `fieldInputKind.test.ts`: each interface maps to the documented kind; unknown → `readonly`.
- `splitFields.test.ts`: partitions by `translatable`; excludes `isSystem`; ordered by `sort`.
- `buildItemPayload.test.ts`: shared at top level; translatable under `translations[locale]`; create
  always includes default locale; empty non-default locale omitted; update stays partial.
- `parseItemToForm.test.ts`: shared from top-level keys; translatable from `translations[locale]`;
  missing locale seeded empty.
- `validateItem.test.ts`: required non-translatable flagged when empty; default-locale required
  translatable flagged; non-default missing → not blocking; valid model → empty map.

**Frontend components (Vitest + Vue Test Utils)**
- `FieldInput.test.ts`: renders the correct control per kind; `readonly`/unsupported → read-only
  display; `readOnly` field → disabled control; `v-model` round-trips.
- `ItemForm.test.ts`: shared fields render once; one tab per locale; translatable fields bind to the
  active locale; invalid submit is blocked with messages; a server error shows in the banner; emits the
  expected payload shape.
- `ItemFormView.test.ts`: create path builds a blank model + calls `create`; edit path calls `get` and
  inflates; `!canWrite` → disabled + no Save; delete confirm → `remove`; a missing item → not-found card.
- Existing `CollectionListView.test.ts`: row click navigates to `collection-item`; "New" shows only
  when `canWrite`.

**API (Vitest)**
- `itemsApi.test.ts` (extend): `get`/`create`/`update`/`remove` hit the right method+path and unwrap.
- `apiClient.test.ts` (extend): `put`/`delete` unwrap, throw on non-2xx, `delete` tolerates 204.
- `languagesApi.test.ts`: `getEnabled` returns the list.

**E2E (Playwright, same-origin dev-proxy path)**
- `items.spec.ts`: login → open `article` list → "New" → fill default-locale `title`/`body` + `status`
  → save → the new row appears → open it → edit a field → save → delete → row gone. This exercises the
  i18n-gated create that Phase 7b could not complete. Seeded data + admin creds as in 7b's `e2e/README.md`.

## §8 Verification gate (evidence required, §17.2)

- Backend: `dotnet build` clean (warnings-as-errors); `dotnet test` all green (prior + `LanguagesEndpointTests`).
- Frontend: `pnpm test` green; `pnpm build` succeeds; `pnpm e2e` passes.
- **Live gate:** because this touches i18n write rules + RBAC + DB, re-verify on **live Postgres +
  Redis**: create an `article` with `en` + `zh-TW` translations, edit it, delete it; confirm the
  translation round-trip (`GET …/{id}` returns both locales) and that create without a default-locale
  translation is correctly rejected. Record evidence.
- Version policy (§17.5): any added frontend package installed via `pnpm add` (no hand-authored
  versions). 7c's scalar set needs no new runtime dependency (TipTap arrives in 7d).

## §9 Self-review notes

- **YAGNI:** relations, files, TipTap, multi-value selects, and structured editors are explicitly
  deferred to 7d and render read-only meanwhile; RichText uses a Textarea fallback. No speculative
  abstraction beyond the dispatcher, which pays for itself when 7d adds renderers.
- **Dependency rule:** backend change is confined to `Struo.Api` (a thin controller over the existing
  `ILanguageProvider`); no Domain/Application contract change. Frontend wire DSL stays inside `itemsApi`
  + the payload helpers; views/components never touch raw payload strings.
- **Ambiguities resolved:** unified edit page (no separate read-only detail); locale **tabs** for
  translatable editing; enabled locales sourced from a dedicated `GET /api/languages` (not the
  RBAC-gated `language` collection); translatable fields detected via the existing `FieldMeta.translatable`
  flag (they are already merged into `fields[]`).
</content>
