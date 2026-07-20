# FE-R3 — Dashboard (frontend redesign, slice 3)

> **Status:** design (brainstormed, approved 2026-07-20).
> **Slice of:** the **frontend admin redesign** (see FE-R0 §1 decomposition). This is **slice 3
> (FE-R3): the Dashboard**, rebuilt on the FE-R0 design system + FE-R1 app shell in the established
> design language, replacing the placeholder `DashboardView.vue`.
> **Design-reference rule (user):** the prototype
> (`docs/struo-cms-frontend-design/struocms-admin-prototype.html`, dashboard section) is a *visual*
> reference only — **no code is copied from it**; the screen is rebuilt from the design.

## 0. Summary

Rebuild the placeholder dashboard into the prototype's four-widget layout — **stat cards**, **recent
updates**, and **quick actions** — populated with **honest, real data sourced purely on the frontend**.
No backend, API, route, or persistence change → **no `dotnet` gate**. A live smoke run is still
recommended (§9) because the frontend fan-out relies on real API behaviour.

**Data strategy (approved).** Pure frontend + honest data only. Two prototype widgets are deliberately
**dropped** because StruoCMS has no data model behind them:

- **Publish status** (`published` / `draft` / `scheduled`) — StruoCMS has **no publish-status concept**
  (no `status` field on any collection). The stat cards "已發布/草稿" and the recent-updates status
  column are removed.
- **Locale coverage** — honest per-item/per-locale translation completeness needs a backend aggregate
  endpoint that does not exist. Deferred to a future backend slice; its right-column slot is removed.

## 1. Scope

**In scope**
- Rebuild `src/views/DashboardView.vue` on the design system (page-head + stats row + dash-grid).
- Frontend fan-out data loading (stat counts + recent updates) via the existing `itemsApi`.
- Quick-actions card (permission-gated navigation).
- `dashboard` i18n namespace (zh-TW + en, symmetric keys).

**Out of scope** (recorded so they aren't lost)
- Locale coverage widget (future backend slice).
- Publish/draft/scheduled status (no data model).
- Personalised greeting name — `/auth/me` (`CurrentUserDto`) exposes only `{ id, isSuperAdmin,
  permissions }`, **no name/email** → the greeting is time-of-day only, no name.
- Any backend aggregate endpoint (`GET /api/dashboard`), caching, pagination, charts.

## 2. Layout (mirrors prototype, rendered via the design system)

```
┌ page-head ────────────────────────────────────────────────┐
│ breadcrumb (FE-R1 mechanism)                               │
│ H1 儀表板   ·  time-of-day greeting (no name)              │
├ stats row (grid auto-fit, minmax ~190px) ─────────────────┤
│ [項目總數] [集合數] [媒體] [使用者]                          │
├ dash-grid (2 col; stacks on mobile) ──────────────────────┤
│ ┌ 最近更新 (wider) ─────────┐  ┌ 快速動作 ──────────────┐ │
│ │ DataTable: 標題/集合/更新 │  │ 上傳媒體 / 新增 {label} │ │
│ └───────────────────────────┘  └─────────────────────────┘ │
└───────────────────────────────────────────────────────────┘
```

Colours/spacing/radius come from the FE-R0 token layer + custom Aura preset; light/dark flip in
lockstep. Cards reuse the shell's card styling established in FE-R1.

## 3. Data sources (frontend fan-out — no backend)

### 3.1 Collection classification
- **`SYSTEM_COLLECTIONS = ['file', 'user']`** — new constant. On the dashboard both are treated as
  system: they get their own stat cards and are **excluded** from "content items total", "collections
  count", and "recent updates" (so the 使用者 card does not double-count into 項目總數, and password
  changes don't flood recent content updates). Note this differs from `buildNav`, which only
  hardcodes-excludes `'file'`; the dashboard additionally excludes `'user'`.
- **Content collections** = `schemaStore.collections` filtered to `canRead(name) === true` **and**
  `name ∉ SYSTEM_COLLECTIONS`.

### 3.2 Fan-out (concurrent, `Promise.allSettled`)
For each content collection, one call:
`itemsApi.list(name, { page: 0, rows: 8, sort: '-updatedAt', locale: defaultLocale })` → `{ data, total }`.
This single batch yields both the per-collection `total` and its most-recent rows.

- **項目總數 (items total)** = Σ `total` over content collections.
- **集合數 (collections count)** = number of content collections.
- **最近更新 (recent updates)** = merge all returned `data`, sort by `updatedAt` descending, take the
  first 8. Each row: **標題** (`resolveItemTitle`), **集合** (collection `label`), **更新時間**
  (`updatedAt`, formatted).
- **媒體 (media)** = `canRead('file')` ? `itemsApi.list('file', { page:0, rows:1 }).total` : card hidden.
- **使用者 (users)** = `canRead('user')` ? `itemsApi.list('user', { page:0, rows:1 }).total` : card hidden.

`locale` = the content default language (`languageStore.defaultCode`) so titles resolve to the default
translation, consistent with existing list/media views.

### 3.3 Verified API facts (why this works without backend)
- `updatedAt` **is projected**: `MetadataScanner.BuildSystemField` marks audit fields
  `IsSystem = true, Hidden = false`; `ItemProjector` emits non-hidden readable fields, so item JSON
  carries `updatedAt`.
- `sort=-updatedAt` **is accepted**: `QueryValidator` enforces the `Sortable` flag **only for relation
  paths**; a scalar sort only requires the field be a known non-hidden field, and `updatedAt` is in that
  allowlist. (The `Sortable` metadata flag is an advisory UI hint, not a query gate.)
- `itemsApi.list` returns `{ data, total }` (`total` from `meta.total`).
- `authStore` exposes `canRead/canWrite/canDelete` getters for safe per-card/per-collection gating.

### 3.4 Permissions & safety
All queries target only readable collections (or are gated by `canRead`), so the fan-out cannot 403 on
a collection the user can't see. Cards for collections the user can't read are hidden, not errored.

## 4. Component decomposition (many small files — §coding-style)

| File | Responsibility |
|---|---|
| `views/DashboardView.vue` | Layout + load orchestration (calls the composable, renders cards). |
| `composables/useDashboardData.ts` | Fan-out loading; returns `{ stats, recent, loading, error }` reactive state. Impure (calls API); thin. |
| `lib/aggregateRecentUpdates.ts` | Pure: merge rows from N collections, sort by `updatedAt` desc, take first N. Unit-tested. |
| `lib/resolveItemTitle.ts` | Pure: resolve a row's display title from `defaultDisplayField` (translatable-aware) with id fallback. `resolveDisplayLabel` is refactored to reuse it (shared `readField`/title logic). |
| `lib/quickActions.ts` | Pure: compute the quick-action list from permissions + collections (upload media + up to 3 writable content collections). Unit-tested. |
| `components/dashboard/StatCard.vue` | One stat card: caption + big mono number (+ optional sub-caption). |
| `components/dashboard/RecentUpdatesTable.vue` | DataTable (標題/集合/更新時間); row click → `collection-item`. |
| `components/dashboard/QuickActions.vue` | Permission-filtered navigation buttons; empty-state text. |

The composable keeps `DashboardView.vue` focused on layout; the three `lib/*` pure functions carry all
testable logic; the three dashboard components are presentational.

## 5. Quick actions (honest, generic)

Built by `lib/quickActions.ts` from permissions + collection metadata:
- **上傳媒體** → route `media` — included when `canWrite('file')`.
- **新增 {label}** → route `collection-create` with `params: { name }` — for each writable content
  collection (`canWrite(name)` && content), **capped at 3** to avoid overflow.
- If the resulting list is empty (no writable targets), the card shows an empty-state message instead of
  buttons.

## 6. i18n

New `dashboard` namespace in `src/locales/zh-TW.ts` + `src/locales/en.ts` (**symmetric keys** — guarded
by the existing recursive key-parity test). Keys:
- `dashboard.title`
- `dashboard.greeting.morning` / `.afternoon` / `.evening` (time-of-day, no name)
- `dashboard.stats.items` / `.collections` / `.media` / `.users`
- `dashboard.recent.title` / `.viewAll` / `.colTitle` / `.colCollection` / `.colUpdated` / `.empty`
- `dashboard.quick.title` / `.uploadMedia` / `.newItem` (with `{label}` param) / `.empty`
- `dashboard.loading` / `dashboard.error`

Time-of-day thresholds are resolved by a tiny pure helper (`lib/greeting.ts` or inline in the composable
— pure, testable): morning `<12`, afternoon `<18`, else evening.

## 7. Error & empty states

- Whole-screen `loading` flag drives a lightweight skeleton/placeholder while the fan-out runs.
- `Promise.allSettled` isolates failures: a single collection's failed query is skipped (logged to the
  UI-facing `error` only if *all* content queries fail); the rest render normally.
- No recent updates → recent table shows `dashboard.recent.empty`.
- No quick-action targets → `dashboard.quick.empty`.
- Media/users cards hidden (not errored) when not readable.

## 8. Testing strategy (TDD)

**Unit (vitest, new)**
- `aggregateRecentUpdates`: merges multiple collections, sorts by `updatedAt` desc, caps at N; handles
  empty input and missing/degenerate `updatedAt` (rows without it sort last, never throw).
- `resolveItemTitle`: `defaultDisplayField` hit (plain + translatable via `translations[locale]`), id
  fallback, empty fallback. Plus regression: `resolveDisplayLabel` still passes after the refactor.
- `quickActions`: includes upload only when `canWrite('file')`; emits "新增" per writable content
  collection; caps at 3; excludes system collections; empty when nothing writable.
- `useDashboardData` (mock `itemsApi`): sums `total` across content collections; hides media/users cards
  when not readable; `allSettled` tolerance (one rejecting collection doesn't fail the load); recent list
  is aggregated + capped.

**Component (vitest)**
- `StatCard`, `RecentUpdatesTable` (renders rows, row click emits/navigates), `QuickActions` (renders
  gated buttons + empty state).
- `DashboardView` integration (mocked composable/API): renders the four cards, recent table, quick
  actions; respects hidden cards.

**Regression:** existing **399** frontend tests stay green; `pnpm build` (vue-tsc + vite) clean
(pre-existing chunk-size advisory only).

**Live smoke (recommended, §9).**

## 9. Acceptance gate

- `pnpm test` all green (399 + FE-R3 new tests); `pnpm build` clean.
- Backend untouched → **no `dotnet` gate**.
- **Live smoke (recommended, per prior FE-slice convention):** against real PostgreSQL + Playwright MCP,
  logged in — confirm: (1) stat cards show real counts (items total = sum of collection totals; media;
  users); (2) recent-updates table lists the most-recently-updated items across content collections,
  newest first, with resolved titles + collection labels + formatted `updatedAt`; (3) quick actions
  navigate correctly and respect permissions; (4) dark/light both read correctly; (5) a low-privilege
  (non-super-admin) user sees only permitted cards/rows with no 403 noise.

## 10. Files

**Added:** `composables/useDashboardData.ts`, `lib/aggregateRecentUpdates.ts`, `lib/resolveItemTitle.ts`,
`lib/quickActions.ts`, `lib/greeting.ts` (or inline), `components/dashboard/StatCard.vue`,
`components/dashboard/RecentUpdatesTable.vue`, `components/dashboard/QuickActions.vue`, plus their tests.
**Changed:** `views/DashboardView.vue` (rebuild), `lib/resolveDisplayLabel.ts` (reuse
`resolveItemTitle`), `locales/zh-TW.ts` + `locales/en.ts` (dashboard namespace).
**Not touched:** backend, any API, route structure, other views/components.
