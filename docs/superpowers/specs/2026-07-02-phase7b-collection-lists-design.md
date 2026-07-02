# Phase 7b — Collection Lists (browse-only admin SPA) Design

> **Status:** design (spec). Follows the phase cycle brainstorm → write-plan → execute → verify (§17.1).
> **Predecessor:** Phase 7a (frontend foundation + auth) — merged to `main`. This phase fills the
> `DashboardView` placeholder that literally reads *"Collection management arrives in Phase 7b."*
> **Successor:** Phase 7c (item detail + create/edit forms + mutations).

## §0 Goal & scope

Give the admin SPA a **schema-driven, browse-only** content surface: a left navigation of the
collections the signed-in user may read, and — per collection — a server-paginated, sortable,
globally-searchable data table.

**In scope**
- Extend `GET /api/auth/me` (additive) to return the caller's effective permissions.
- Frontend: typed `schemaApi`/`itemsApi`, a `schemaStore`, pure list-logic helpers, a
  `CollectionNav`, and a `CollectionListView` (route `/collections/:name`) using PrimeVue `DataTable`
  in lazy (server-side) mode with pagination, sort, and global search.
- RBAC-aware navigation (nav lists only readable collections; super-admin sees all).

**Out of scope (deferred)**
- Create / edit / delete (mutations) and item **detail** views → Phase 7c.
- Per-column / advanced filter UI (the `filter[field][_op]` DSL surface) → later.
- i18n **locale switcher** and translated columns → later i18n-aware UI phase.
- File thumbnails / TipTap rendering → later phases.

## §1 Backend change — expose effective permissions on `/me` (additive)

Today `GET /api/auth/me` returns `{ data: { id } }`. Extend it to project the **already-resolved**
per-request permission snapshot:

```jsonc
{ "data": {
    "id": "…",
    "isSuperAdmin": false,
    "permissions": {
      "article":  { "read": true,  "write": true,  "delete": false },
      "category": { "read": true,  "write": false, "delete": false }
    }
} }
```

- Source: `ICurrentPermissions.Current` (`EffectivePermissions`), populated once per request by
  `PermissionResolutionMiddleware`. `AuthController.Me()` injects `ICurrentPermissions` and projects it.
- `permissions` lists **only collections with at least one grant**. When `isSuperAdmin === true`,
  the map may be sparse/empty — clients treat super-admin as "may read every collection" and must not
  rely on enumerated entries.
- **Additive only:** `id` is unchanged; the two new fields do not alter existing behavior. All prior
  backend tests remain green (SQLite-green ≠ Postgres-correct — permission behavior is re-verified on
  live Postgres+Redis at the gate, §8).
- `EffectivePermissions` shape (existing): `IsSuperAdmin: bool`,
  `byCollection: IReadOnlyDictionary<string, (bool Read, bool Write, bool Delete)>`.

No new endpoint. `/api/schema` stays anonymous and unchanged (it already strips `Hidden` fields);
RBAC filtering of the nav happens client-side from the `/me` snapshot.

## §2 Frontend architecture

Layered exactly as Phase 7a (`api` → `stores` → pure helpers → components → router). One
responsibility per file (CLAUDE.md "many small files"; DSL never leaks into views — §2 of CLAUDE.md,
query-DSL-does-not-leak rule).

**New files**

| File | Responsibility |
|---|---|
| `src/types/schema.ts` | TS types mirroring backend DTOs: `CollectionMeta`, `FieldMeta`, `FieldInterface`, `FieldOption`, `EffectivePermissionsDto`. |
| `src/api/schemaApi.ts` | `getAll(): Promise<CollectionMeta[]>`, `get(name): Promise<CollectionMeta \| null>` over `apiClient`. |
| `src/api/itemsApi.ts` | `list(name, opts): Promise<ListResult>` where `opts = { page, rows, sort?, search? }`; wraps `GET /api/items/{name}`. Returns `{ data: Record<string,unknown>[], total: number }`. |
| `src/stores/schemaStore.ts` | Pinia store: `collections`, `load()` (fetch once + cache), `get(name)`, `loadError`. |
| `src/lib/buildListQuery.ts` | Pure: `(page, rows, sort?, search?) → Record<string,string>` query params. |
| `src/lib/selectListColumns.ts` | Pure: `(meta) → ColumnDef[]` (smart-subset rule, §5). |
| `src/lib/formatCell.ts` | Pure: `(value, field) → string` (Select→label, DateTime→formatted, null→"—"). |
| `src/lib/buildNav.ts` | Pure: `(collections, isSuperAdmin, permissions) → NavGroup[]` (RBAC filter + group by `Group`). |
| `src/components/CollectionNav.vue` | PrimeVue `PanelMenu`; items from `buildNav`; navigates to `/collections/:name`. |
| `src/views/CollectionListView.vue` | Route `/collections/:name`; PrimeVue lazy `DataTable`; wires the helpers + `itemsApi`. |

**Modified files**

- `src/stores/authStore.ts` — `CurrentUser` gains `isSuperAdmin: boolean` and
  `permissions: Record<string, {read;write;delete}>`; `fetchCurrentUser` stores them. `login`/`logout`
  unchanged in contract.
- `src/layouts/AppShell.vue` — mount `<CollectionNav />` in the existing `<nav>` placeholder; trigger
  `schemaStore.load()` on mount.
- `src/router/index.ts` — add child route of `AppShell`: `path: 'collections/:name'`, name
  `collection-list`, component `CollectionListView`. `dashboard` route retained.

## §3 Data flow

**Boot / login**
1. `main.ts` calls `authStore.fetchCurrentUser()` before mount (7a); it now also stores
   `isSuperAdmin` + `permissions`.
2. On first protected route, `AppShell` calls `schemaStore.load()` → `GET /api/schema` (once, cached).
3. `CollectionNav` derives items via `buildNav(collections, isSuperAdmin, permissions)`: filter
   (super-admin → all; else `read === true`), then group by `Group` (missing `Group` → an
   "Ungrouped"/general section).

**Browse a collection** (`/collections/:name`)
1. `CollectionListView` reads the collection meta from `schemaStore`; `selectListColumns(meta)` →
   columns.
2. PrimeVue `DataTable` runs **lazy**: initial load + every `@page` / `@sort` / (debounced) search
   input calls `loadItems()`.
3. `loadItems()` → `buildListQuery(page, rows, sort, search)` → `itemsApi.list(name, …)` →
   `GET /api/items/{name}?limit=&offset=&sort=&search=`.
4. Response `{ data, meta: { total } }` → rows bind to `data`; `total` binds to the paginator's
   `totalRecords` (server-side; the client never holds the full set).
5. Cells render through `formatCell(value, field)`.

**Query mapping** (`buildListQuery` → existing backend DSL)
- Pagination: `limit = rows`, `offset = page * rows`.
- Sort: `sort = field` (asc) or `sort = -field` (desc); omitted when unsorted.
- Search: `search = <text>` (backend matches `Searchable` fields); omitted when empty.

**Switching collections**: `:name` changes → reset `page=0`, `sort`, `search` → `loadItems()`. Meta
comes from the cached `schemaStore` (no re-fetch of `/schema`).

## §4 List transport decision — GET now, POST envelope later

Listing uses `GET /api/items/{name}` with query-string params for 7b (short URLs, cacheable,
simplest). The backend also offers `POST /api/items/{name}/query` (JSON envelope, same DSL) — when
Phase 7c/advanced-filter needs nested `_and`/`_or` or long conditions, `itemsApi.list` switches to the
POST envelope **internally**; because the DSL is encapsulated in `itemsApi` + `buildListQuery`, no
view/component changes. This is the payoff of the abstraction: the UI calls ergonomic methods
(`list`, and later `get`/`create`/`update`/`remove`), never raw DSL strings.

## §5 Smart-subset column rule (`selectListColumns`)

Given a `CollectionMeta`, produce ordered `ColumnDef[]`:
1. `DefaultDisplayField` (if present and not hidden) first.
2. Then remaining fields where `!IsSystem && !Hidden` and the interface is **scalar**:
   `Text`, `Select`, `DateTime`, `Number`, `Boolean` (and other simple scalar interfaces).
3. **Exclude** `RichText`, relation fields, and `File`/`Image` interfaces (those belong to the 7c
   detail/form).
4. Cap at **6** columns total.
5. Per-column `sortable` = `field.Sortable`.

`formatCell` renders: `Select` → matching `FieldOption` label (fallback to raw value);
`DateTime` → locale-formatted date/time; `Boolean` → localized yes/no; `null`/empty → `"—"`; text →
as-is (truncated for display in the component template, not in the helper).

## §6 Error handling

Built on 7a's `apiClient` (unwraps `{data}`, throws `error.message`, invokes the 401 handler).

- **401 (session expired):** existing unauthorized handler clears the store and routes to `/login`;
  a mid-load 401 is caught by the same path. No 7b-specific handling.
- **403 (RBAC denied):** nav already hides unreadable collections, but two leaks are guarded —
  (a) deep-linking to an unreadable collection, (b) a `/me`↔`/schema` timing skew.
  `CollectionListView` runs a client-side permission check (`authStore`) **before** fetching:
  unreadable → show a "You don't have access to this collection" card, **no API call**. If the check
  is bypassed, the backend still returns 403 → `apiClient` throws → the view shows the same card
  (backend is the final authority; the client check is UX only).
- **404 (unknown collection):** `:name` not found in `schemaStore` → "Collection not found" card, no
  list request.
- **List load failure (500 / network):** `DataTable` leaves loading state; the table region shows a
  retryable error (`error.message`) while preserving search/pagination state. Errors are never
  silently swallowed (CLAUDE.md error-handling rule).
- **Empty results:** `total === 0` → `DataTable` `#empty` slot shows "No records", distinct from the
  error state.
- **Schema load failure:** `schemaStore.load()` fails → the nav region shows an error + retry; the app
  does not crash (nav is empty but usable).

## §7 Testing strategy

Failing-test-first for logic (§17.2). Pure helpers are the test focus; components test interactions;
one backend integration test; one E2E.

**Backend (`tests/Struo.Tests`, TDD)**
- `AuthMePermissionsTests`: after login, `GET /api/auth/me` returns `isSuperAdmin` + `permissions`.
  ≥2 cases: super-admin (`isSuperAdmin=true`), and a limited role (map lists only granted
  collections). Uses the existing SQLite + `WebApplicationFactory<Program>` pattern.
- Regression: existing suite stays green (additive fields).

**Frontend pure helpers (Vitest, red→green) — primary coverage**
- `buildListQuery.test.ts`: pagination → `limit`/`offset`; asc/desc → `sort`/`-sort`; empty
  search/unsorted → params omitted.
- `selectListColumns.test.ts`: `DefaultDisplayField` first; excludes `IsSystem`/`Hidden`/RichText/
  relation/File; caps at 6; carries `Sortable`.
- `formatCell.test.ts`: Select→label; DateTime→formatted; null/empty→"—"; plain text unchanged.
- `buildNav.test.ts`: super-admin keeps all; limited user keeps only `read` collections; groups by
  `Group`; missing `Group` → general section.

**Frontend components (Vitest + Vue Test Utils)**
- `CollectionListView.test.ts`: mock `itemsApi`; `@page`/`@sort`/(debounced) search call `list` with
  correct params; `total` binds the paginator; unreadable collection shows the permission card and
  makes **no** API call; load failure shows the error state.
- `CollectionNav.test.ts`: given collections + permissions, renders correct groups and clickable
  items; unreadable collections absent.
- Existing `AppShell.test.ts`: add a `CollectionNav` stub.

**E2E (Playwright, 7a same-origin dev-proxy path)**
- Extend `auth.spec.ts` or add `collections.spec.ts`: login → nav shows ≥1 collection (sample blog
  `article`) → open it → rows visible → next page → search narrows results → logout. Requires seeded
  data (a few sample `article` rows), documented in `e2e/README.md`.

## §8 Verification gate (evidence required, §17.2)

- Backend: `dotnet build` clean (warnings-as-errors); `dotnet test` all green (255 prior + new).
- Frontend: `pnpm test` green; `pnpm build` succeeds; `pnpm e2e` passes.
- **Live gate:** because this touches RBAC + DB, re-verify the `/me` permissions behavior and a
  collection list on **live Postgres + Redis** (SQLite-green ≠ Postgres-correct). Record evidence.
- Version policy (§17.5): any added frontend package installed via `pnpm add` (no hand-authored
  versions).

## §9 Self-review notes

- **YAGNI:** no mutations, detail, filter UI, locale switch, or file/richtext rendering — all deferred
  with explicit owners.
- **Dependency rule:** backend change is confined to `Struo.Api` (`AuthController` + a scoped port it
  already consumes); no Domain/Application contract change. Frontend DSL confined to `itemsApi` +
  `buildListQuery`.
- **Ambiguity resolved:** "convenient CRUD functions" = the `itemsApi` abstraction; the DSL is only the
  wire format underneath (§4). Column set is the fixed smart-subset rule (§5). Super-admin semantics
  for a sparse `permissions` map are explicit (§1).
