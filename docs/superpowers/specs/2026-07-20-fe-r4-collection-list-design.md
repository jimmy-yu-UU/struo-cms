# FE-R4 — Collection List (frontend redesign, slice 4)

> **Status:** design (brainstormed, approved 2026-07-20).
> **Slice of:** the **frontend admin redesign** (see FE-R0 §1 decomposition). This is **slice 4
> (FE-R4): the Collection List**, rebuilt on the FE-R0 design system + FE-R1 app shell in the
> established design language, re-skinning the existing `CollectionListView.vue`.
> **Design-reference rule (user):** the prototype
> (`docs/struo-cms-frontend-design/struocms-admin-prototype.html`, `view-collection` section) is a
> *visual* reference only — **no code is copied from it**; the screen is rebuilt from the design.

## 0. Summary

Re-skin the collection list to the prototype's layout — **page-head** (title + count caption +
primary action), **toolbar** (search + status filter), styled **DataTable**, and a **table footer**
(range info + paginator) — while preserving **every existing behaviour** of the current
`CollectionListView.vue`. No backend, API, route, or persistence change → **no `dotnet` gate, no live
Postgres gate**. A live smoke run is still recommended (§9) because the list relies on real
lazy-pagination / sort / soft-delete API behaviour.

**Honest-data strategy (approved, consistent with FE-R3).** The prototype's collection list assumes
two concepts StruoCMS's backend does not have; both are **not adopted**:

- **Publish status** (`published` / `draft` / `scheduled`) — StruoCMS has **no publish-status
  concept**. The prototype's status **column** (a coloured tag) is **dropped**.
- **Locale coverage dots** — honest per-item/per-locale completeness needs a backend aggregate that
  does not exist (deferred, same as FE-R3). The prototype's **語系 column** is **dropped**.

The prototype's **"狀態篩選" dropdown** maps to the **only real status concept** StruoCMS has: the
existing **9b-fe soft-delete Active/Trash switch** (already in the view, shown only when
`softDelete && canDelete`). It is re-presented in the new toolbar; no new filter semantics are added.

The table's data **columns stay schema-driven** via the existing `selectListColumns(meta)` — never the
prototype's hardcoded 標題/狀態/語系/更新時間.

## 1. Scope

**In scope**
- Rebuild `src/views/CollectionListView.vue` on the design system (page-head + toolbar + DataTable +
  table-footer), preserving all current behaviour listed in §2.
- Three new **reusable** presentational components under `src/components/common/`:
  - `PageHeader.vue` — title + optional caption + `#actions` slot.
  - `ListToolbar.vue` — search input (with search icon) + `#filters` slot.
  - `TableFooter.vue` — "showing X–Y of N" range info (pure text).
- New `collectionList` i18n namespace (zh-TW default / en); extract the view's hardcoded strings.

**Out of scope (YAGNI)**
- Any backend / API / route / persistence change.
- A publish-status column/filter or locale-coverage column (dropped — see §0).
- A hand-rolled paginator (see §4 — PrimeVue's built-in lazy paginator is retained).
- FE-R5 item form, FE-R6 media, FE-R7 revisions.
- Bulk actions / column chooser / saved views (not in prototype, not requested).

## 2. Preserved behaviour (must not regress)

The current view's logic is correct and stays intact — only the presentation and string-extraction
change. All of the following must continue to work exactly as today:

- **Lazy pagination** via PrimeVue `DataTable` (`lazy` + `paginator`), server-side page/rows.
- **Server-side sort** (`onSort`) and **debounced search** (300 ms trailing, cancel-on-unmount and
  on collection switch).
- **latest-wins guard** (`createLatestWins`) so a slow earlier load cannot clobber a newer one, and
  only the current load may clear the spinner.
- **RBAC gates**: `canRead` (no-access notice), `canWrite` (New button + row-click to edit),
  `canDelete` (actions column + Trash switch visibility).
- **9b-fe soft delete**: Active/Trash `SelectButton` (gated `softDelete && canDelete`); actions column
  — active → Delete; trash → Restore + Delete permanently; `@click.stop`; `deleteKindFor` /
  `deleteConfirm` / `purgeConfirm` copy via `ConfirmDialog`.
- **schema.load() retry** on hard refresh / deep link; **collection-switch reset** (`watch(name)`
  clears page/sort/search/mode and cancels pending search).
- **Translatable cell values** resolved from `translations[defaultCode]` via `cellValue`.
- **Row click** opens the item form (active mode only; ignored in trash).
- **Error surface** (`error` alert) and **empty state**.

## 3. New components (`src/components/common/`)

Each is small, single-purpose, presentational (no store/router/api access), and independently
testable. R5 (item form) and R6 (media) reuse them.

### 3.1 `PageHeader.vue`
- **Props:** `title: string`, `caption?: string`.
- **Slots:** `#actions` (right-aligned action area, e.g. the New button).
- **Renders:** `.page-head` with a `.titles` group (`<h1>{{title}}</h1>` + optional
  `<p class="caption">{{caption}}</p>`) and a `.head-actions` wrapper around the `#actions` slot.
- No caption element rendered when `caption` is falsy.

### 3.2 `ListToolbar.vue`
- **Props:** `searchValue: string`, `searchPlaceholder?: string`.
- **Emits:** `update:searchValue` (or `search`) with the raw input string on every input event —
  debouncing stays the **consumer's** responsibility (the view already owns the debounce).
- **Slots:** `#filters` (right side, e.g. the Active/Trash `SelectButton`).
- **Renders:** `.toolbar` with a search `InputText` wrapped with a `pi pi-search` icon (aria-labelled)
  and the `#filters` slot. Search input has `type="search"` and an accessible label.

### 3.3 `TableFooter.vue`
- **Props:** `first: number` (0-based index of first row on page), `rows: number`, `total: number`.
- **Renders:** a single caption element rendering the `collectionList.range` key (§6) via `t()` with
  named params `{ from, to, total }`, where `from = total === 0 ? 0 : first + 1` and
  `to = min(first + rows, total)`.
- The only computed logic is the `from`/`to`/`total` math; it reads exactly one i18n key and has no
  store/router/api access.

## 4. Paginator approach (decided)

Keep PrimeVue `DataTable`'s **built-in lazy paginator** — the existing `lazy` + `paginator` +
`@page` integration is verified-stable with the latest-wins guard. The prototype's `.tbl-foot`
"range info + pager" look is achieved by rendering `TableFooter` inside the DataTable's
**`#paginatorstart` slot**; the paginator page buttons remain PrimeVue's own (`#paginatorend` or
default). No hand-rolled pager — that would require re-wiring lazy page events and regress §2.

`first` for `TableFooter` is derived as `page * perPage` (the view already tracks `page`/`perPage`).

## 5. `CollectionListView.vue` (rebuild — assembly only)

The script logic is **carried over verbatim** where possible (loadItems / latest-wins / onPage /
onSort / debounced search / soft-delete actions / watch(name) reset / defineExpose). The template is
rebuilt to assemble the new components:

```
<section class="collection-list">
  <ConfirmDialog />
  <p v-if="!meta"      class="notice">{{ t('collectionList.notFound') }}</p>
  <p v-else-if="!canRead" class="notice">{{ t('collectionList.noAccess') }}</p>
  <template v-else>
    <PageHeader :title="meta.label" :caption="t('collectionList.count', { n: total })">
      <template #actions>
        <Button v-if="canWrite" :label="t('collectionList.new')" icon="pi pi-plus" @click="onNew" />
      </template>
    </PageHeader>

    <ListToolbar :search-value="search" :search-placeholder="t('collectionList.searchPlaceholder')"
                 @search="onSearchInput">
      <template #filters>
        <SelectButton v-if="showTrashSwitch" ... />   <!-- Active/Trash, unchanged -->
      </template>
    </ListToolbar>

    <p v-if="error" class="error" role="alert">{{ error }}</p>

    <DataTable lazy paginator ... @page @sort @row-click>
      <Column v-for="col in columns" ... />           <!-- schema-driven, unchanged -->
      <Column v-if="canDelete" ...>...</Column>        <!-- actions, unchanged -->
      <template #empty>{{ t('collectionList.empty') }}</template>
      <template #paginatorstart>
        <TableFooter :first="page * perPage" :rows="perPage" :total="total" />
      </template>
    </DataTable>
  </template>
</section>
```

`defineExpose` keeps its current surface so existing test hooks keep working; new template refs are
added only if a test needs them.

## 6. i18n

New namespace **`collectionList`** in `src/locales/{zh-TW,en}/...` following the existing per-slice
namespace convention. Keys (values illustrative):

| Key | zh-TW | en |
|---|---|---|
| `count` | 共 {n} 筆 | {n} items |
| `new` | 新增 | New |
| `searchPlaceholder` | 搜尋… | Search… |
| `range` | 顯示 {from}–{to} / 共 {total} 筆 | Showing {from}–{to} of {total} |
| `active` | 使用中 | Active |
| `trash` | 回收桶 | Trash |
| `delete` | 刪除 | Delete |
| `restore` | 還原 | Restore |
| `purge` | 永久刪除 | Delete permanently |
| `empty` | 沒有資料 | No records |
| `notFound` | 找不到集合 | Collection not found |
| `noAccess` | 您沒有此集合的存取權 | You don't have access to this collection |

`TableFooter` renders `range` with named params; `PageHeader` caption uses `count`. Final wording is
finalized during implementation; the "New" label may append the collection label if it reads well,
but stays a single key to keep the component generic.

## 7. Styling

- Layout classes (`.page-head`, `.titles`, `.caption`, `.head-actions`, `.toolbar`, table footer)
  are styled with the FE-R0 **OKLch layout tokens** (`--fg`, `--muted`, `--border`, `--surface`,
  `--radius`, spacing), matching the prototype's proportions — **not** copied CSS.
- The `DataTable`, `InputText`, `Button`, `SelectButton`, paginator, and `ConfirmDialog` keep their
  PrimeVue Aura preset skin (FE-R0). Component-scoped styles only; no global bleed.
- Search icon uses **primeicons** (`pi pi-search`); light/dark flip in lockstep with the token layer.

## 8. Testing

- **Unit** — one spec per new component:
  - `PageHeader`: renders title; caption shown only when provided; `#actions` slot content mounts.
  - `ListToolbar`: emits raw value on input; renders search icon + accessible label; `#filters` slot
    mounts.
  - `TableFooter`: range math (`from`/`to`/`total`, incl. `total === 0` → 0, last partial page).
- **View** — rewrite `CollectionListView.test.ts` to cover the preserved behaviour of §2 through the
  new template: lazy load, search debounce path, sort, RBAC gates, Active/Trash switch + soft-delete
  actions, translatable cell, not-found / no-access notices, empty + error states, range footer.
- **Gate** — `pnpm build` (vue-tsc typecheck) **and** `pnpm test` (vitest) both green. Per the FE
  lesson: vitest strips types → run `pnpm build` too. FE test count expected to grow.

## 9. Live verification (recommended, not a hard gate)

Pure-frontend, so no DB gate — but a Playwright MCP smoke on real PG/backend is recommended because
the list depends on real lazy pagination / sort / search / soft-delete round-trips:

- Backend `ASPNETCORE_URLS=:5080` (Vite proxy default), bootstrap admin; `pnpm dev --host 127.0.0.1`.
- Use the `plugin_playwright` MCP (not the ECC bridge), logged-in.
- Verify on a populated content collection: page-head title + "共 N 筆", search filters + range info
  updates, sort, paginate, Active/Trash switch (on a `softDelete` collection) + Delete → Trash →
  Restore / Delete permanently, dark/light. Isolate rows by a stable Title if the DB is populated.

## 10. Risks / notes

- **Search debounce ownership** — `ListToolbar` emits raw input; the **view** keeps the existing
  300 ms debounce + cancel-on-switch. Moving debounce into the toolbar would duplicate/hide it —
  explicitly kept in the view.
- **`#paginatorstart` slot** — confirm the range text sits left of the pager as the prototype's
  `.tbl-foot` intends; if PrimeVue slot placement fights the layout, fall back to a footer row
  rendered under the DataTable while still using the built-in paginator for page control.
- **defineExpose surface** — keep current exposed members so existing test expectations don't break;
  add refs only as tests require.
- **No new deps** — reuse primeicons + existing PrimeVue components; no `pnpm add`.
