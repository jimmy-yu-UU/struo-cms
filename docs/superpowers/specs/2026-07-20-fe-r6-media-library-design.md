# FE-R6 — Media Library (frontend redesign, slice 6)

> **Status:** design (brainstormed, approved 2026-07-20).
> **Slice of:** the **frontend admin redesign** (see FE-R0 §1 decomposition). This is **slice 6
> (FE-R6): the Media Library**, rebuilt on the FE-R0 design system + FE-R1 app shell in the established
> design language, re-skinning the existing `MediaLibraryView.vue` + `components/media/*`.
> **Design-reference rule (user):** the prototype
> (`docs/struo-cms-frontend-design/struocms-admin-prototype.html`, `data-view="media"` section +
> `dlg-media` / `dlg-upload` dialogs) is a *visual* reference only — **no code is copied from it**; the
> screen is rebuilt from the design.

## 0. Summary

Re-skin the media library to the prototype's design language — a **page-head action bar**
(title + file count + "Upload" button), a **toolbar** (search / type filter / sort / grid·list view
toggle), a **media grid or list** with **server-side pagination**, a **file-detail dialog** (preview,
metadata, copy URL, delete, **per-locale Title/Alt editing**), and an **upload dialog** (the drag-drop
dropzone, moved off the always-open page top) — while preserving every existing behaviour of the
current view and the shared media components. No backend, route, or dependency change → **no `dotnet`
gate**. A live Playwright smoke run on real Postgres is still done (§8) because the screen depends on
real upload / list / update / 409-recovery / delete round-trips.

The current `MediaLibraryView.vue` is a placeholder: a raw `<h1>`, an always-open dropzone, a flat row
of Edit/Delete buttons for *all* files, hardcoded English, and no search / filter / sort / view toggle
/ detail dialog. FE-R6 rebuilds it into a real media manager on the established design system.

**Honest-data strategy (approved, consistent with FE-R3/FE-R4/FE-R5).** The prototype's media screen
assumes concepts the backend does not cleanly support; those are not adopted literally:

- **Type filter — 圖片 / 文件 / 影音** → reduced to **全部 / 圖片 / 影音**. `image/` and `video/` map
  cleanly to a single `contentType _starts_with` server-side condition; **"文件"** does not (it is
  `application/*` + `text/*` minus others, and there is **no `NotStartsWith` operator**), so forcing a
  "documents" bucket would be dishonest. It is dropped. (Decision approved.)
- **Dashboard media aggregate ("342 files · 1.2 GB")** — a prototype dashboard stat, not part of this
  slice; not adopted here.

## 1. Scope

**In scope**
- Rebuild `src/views/MediaLibraryView.vue` (assembly + state + string-extraction): `PageHeader` with an
  Upload action, toolbar, grid/list body, pagination, detail dialog, upload dialog.
- Re-skin `src/components/media/MediaGrid.vue` to the design system; **keep** its existing
  `selectable` / `multiple` / `selectedId` / `selectedIds` props + `select` / `toggle` emits (shared
  with `FilePicker`), and **add** an `open` emit fired on a plain (non-selectable) tile click so the
  library can open the detail dialog. `FilePicker` behaviour must not regress.
- Re-skin `src/components/media/FileThumbnail.vue` (design tokens; keep the `FileRow` type + image /
  non-image chip behaviour).
- Re-skin `src/components/media/MediaUploadDropzone.vue` (design-token error colour instead of `#d33`;
  keep the parallel-upload + per-row error + `uploaded`/`done` emit behaviour verbatim).
- New `src/components/media/MediaUploadDialog.vue` — wraps `MediaUploadDropzone` in a PrimeVue `Dialog`;
  opened by the page-head Upload button; re-emits `done` to trigger a reload.
- New `src/components/media/MediaDetailDialog.vue` — file detail + **per-locale Title/Alt editing**
  (§3.4).
- New `src/components/media/MediaFileList.vue` — the **list** view mode (library-only; the picker keeps
  grid only). PrimeVue `DataTable` with thumbnail / filename / type / size / dimensions / uploaded
  columns; row click → open detail dialog.
- New `media` i18n namespace (zh-TW default / en); extract all hardcoded strings.

**Out of scope (YAGNI)**
- Any backend / API / route / dependency change (no `dotnet` build, no `pnpm add`).
- The **"文件"** type bucket (dropped — see §0).
- Editing file **Status** (draft/published/archived) inside the detail dialog — the dialog links out to
  the full item form (`/collections/file/:id`) for status and any advanced editing (§3.4).
- Bulk selection / bulk delete in the library view (per-file delete only, as today).
- Folders, tags, replace-file-in-place, image cropping, usage/references view.
- Changes to the field-type pickers (`fields/FilePicker.vue`, `FileField.vue`, `FilesField.vue`) beyond
  what a `MediaGrid` re-skin transparently gives them.

## 2. Preserved behaviour (must not regress)

- **`MediaGrid` picker contract** — `FilePicker` (and `FileField`/`FilesField`) drive `MediaGrid` in
  `selectable` / `multiple` modes and rely on `select` / `toggle` emits + `is-selected` styling. The
  re-skin keeps this contract byte-for-byte; only styling changes and the additive `open` emit are new.
- **Upload** — `MediaUploadDropzone` uploads N files in parallel via `filesApi.upload`
  (`POST /files`, multipart part name `file`), shows a per-file uploading/error row, and emits
  `uploaded` per file + `done` once. Logic carried over verbatim.
- **Delete** — permanent `filesApi.remove` (`DELETE /files/{id}`) behind a `deleteConfirm('hard')`
  `ConfirmDialog`, then reload. (Moves from the flat button row into per-tile / detail-dialog actions.)
- **List load** — `itemsApi.list('file', …)` for the `file` collection, RBAC-gated on
  `auth.canWrite('file')` / `auth.canDelete('file')`; content locale from `languageStore.defaultCode`.
- **Edit round-trip** — reuse `itemsApi.get` + `parseItemToForm` (load) and `buildItemPayload` +
  `itemsApi.update` (save), the same helpers the item form uses, including the `version` concurrency
  token for 409 handling.

## 3. Components

### 3.1 `MediaLibraryView.vue` (rebuild — assembly + state)

State: `files`, `total`, `loading`, `error`, `page`, `perPage` (24), `search`, `type`
(`'all' | 'image' | 'video'`), `sort` (`'newest' | 'name'`), `view` (`'grid' | 'list'`),
`selected` (the `FileRow | null` open in the detail dialog), `uploadOpen`. Search is debounced (shared
`debounce`, 300 ms, cancel-on-unmount) and loads use `createLatestWins` to guard against out-of-order
responses — both exactly as `CollectionListView` (FE-R4).

Template shape (assembled from shared + new pieces; illustrative, not copied CSS):

```
<section class="media-library">
  <ConfirmDialog />
  <PageHeader :title="t('media.title')" :caption="t('media.count', { n: total })">
    <template #actions>
      <Button v-if="canWrite" :label="t('media.upload')" icon="pi pi-upload" @click="uploadOpen = true" />
    </template>
  </PageHeader>

  <ListToolbar :search-value="search" :search-placeholder="t('media.searchPlaceholder')" @search="onSearchInput">
    <template #filters>
      <Select v-model="type" :options="typeOptions" option-label="label" option-value="value" @change="reload" />
      <Select v-model="sort" :options="sortOptions" option-label="label" option-value="value" @change="reload" />
      <SelectButton v-model="view" :options="viewOptions" option-label="…" option-value="value" :allow-empty="false" />
    </template>
  </ListToolbar>

  <p v-if="error" class="error" role="alert">{{ error }}</p>

  <MediaGrid v-if="view === 'grid'" :files="files" @open="openDetail" />
  <MediaFileList v-else :files="files" @open="openDetail" />
  <p v-if="!loading && !files.length" class="empty">{{ t('media.empty') }}</p>

  <TableFooter :first="page * perPage" :rows="perPage" :total="total" />
  <Paginator :rows="perPage" :total-records="total" :first="page * perPage" @page="onPage" />

  <MediaUploadDialog v-model:visible="uploadOpen" @done="reload" />
  <MediaDetailDialog
    :file="selected" :can-write="canWrite" :can-delete="canDelete"
    @close="selected = null" @saved="reload" @deleted="onDeleted" />
</section>
```

The exact toolbar assembly (whether the three controls sit in `ListToolbar #filters` or a dedicated
`.media-toolbar`) is finalized in implementation; `ListToolbar`'s search + `#filters` slot is reused if
it fits, otherwise the toolbar is composed with the same tokens. Pagination reuses `TableFooter` for the
"showing X–Y of N" line (FE-R4 pattern).

### 3.2 `MediaGrid.vue` (re-skin, keep picker contract)

- Re-skin `.media-grid` / `.media-tile` / `.media-tile__name` to FE-R0 layout tokens (spacing, border,
  radius, selected outline = accent token). Keep the `auto-fill minmax` responsive grid.
- Keep `selectable` / `multiple` / `selectedId` / `selectedIds` props and `select` / `toggle` emits.
- **Add** `(e: 'open', id: string)` emit. `onClick`: `multiple` → `toggle`; else `selectable` →
  `select`; **else → `open`** (the library's non-selectable mode). The picker never passes no-mode, so
  `open` is inert there.

### 3.3 `MediaFileList.vue` (new — list view mode, library-only)

PrimeVue `DataTable` over the same `FileRow[]`: a small thumbnail column (`FileThumbnail`), file name,
content type, size (human-readable via a small formatter), dimensions (`w×h` when present), and uploaded
date. Row click emits `open`. Non-paginated itself (the view owns pagination); purely presentational.

### 3.4 `MediaDetailDialog.vue` (new — detail + per-locale Title/Alt)

Opened with a `FileRow` (id known). On open it loads the full item via
`itemsApi.get('file', file.id)` and maps it with `parseItemToForm(fileMeta, item, locales)` to get a
`FormModel` whose `translations[locale]` carries **Title** and **Alt** per locale, plus the `version`
token. `fileMeta` comes from `schemaStore.get('file')`; `locales` from `languageStore.languages`.

Layout (PrimeVue `Dialog`, wide):
- **Preview** — image via `filesApi.contentUrl(id)`, else a file chip (reuse `FileThumbnail`).
- **Per-locale Title / Alt** — a compact locale switcher (the content `languageStore` locales; a
  `SelectButton` or `Tabs`) selecting which locale's `Title` + `Alt` text inputs are edited. Bound to
  `model.translations[activeLocale]`. Only these two translatable fields are edited here.
- **Metadata (read-only)** — dimensions (`w×h`), size (human-readable), uploaded date (`createdAt`),
  status. Shown as key/value rows; **not editable** here.
- **File URL** — read-only input + copy button (`navigator.clipboard.writeText`; on success a
  `media.urlCopied` toast; graceful fallback if the clipboard API is unavailable).
- **Footer** — Delete (danger, gated `canDelete`, `deleteConfirm('hard')` → `filesApi.remove` →
  emit `deleted`), Save (gated `canWrite`), and a text link **"Open in full editor"** →
  `router.push({ name: 'collection-item', params: { name: 'file', id } })` for status / advanced edits.

Save: `buildItemPayload(fileMeta, model, locales, 'update')` → `itemsApi.update('file', id, payload)`
(sends only locales with content + the `version` token). On success emit `saved` and close. **409
handling** mirrors the item form: on `VERSION_CONFLICT` show a non-destructive banner/toast
("file was changed elsewhere — reopen to get the latest") and let the user re-open (a full
merge/reload-latest flow is out of scope for the dialog — reopening re-fetches the current version).
Other server `details` map to per-field (Title/Alt) errors where applicable, else a dialog-level error.

### 3.5 `MediaUploadDialog.vue` (new) + `MediaUploadDropzone.vue` (re-skin)

- `MediaUploadDialog` wraps `MediaUploadDropzone` in a PrimeVue `Dialog` (`v-model:visible`), opened by
  the page-head Upload button. On the dropzone's `done` it re-emits `done` (view reloads) and may
  auto-close after a successful batch (finalized in implementation; keep it open if any row errored so
  the user sees the failures).
- `MediaUploadDropzone` keeps its parallel-upload + per-row state logic verbatim; only styling changes:
  drop `#d33` for the FE-R0 **danger** status token, align dropzone borders/spacing with the design
  system. Keep drag-over accent, the hidden file input, and `uploaded`/`done` emits.

## 4. i18n

New namespace **`media`** in `src/locales/{zh-TW,en}.ts` (per-slice namespace convention;
`locales.test.ts` parity enforced). Keys (values illustrative, finalized in implementation):

| Key | zh-TW | en |
|---|---|---|
| `title` | 媒體庫 | Media Library |
| `count` | {n} 個檔案 | {n} files |
| `upload` | 上傳檔案 | Upload |
| `searchPlaceholder` | 搜尋檔名… | Search files… |
| `typeAll` | 全部類型 | All types |
| `typeImage` | 圖片 | Images |
| `typeVideo` | 影音 | Video |
| `sortNewest` | 最新上傳 | Newest |
| `sortName` | 依名稱 | By name |
| `viewGrid` | 網格檢視 | Grid view |
| `viewList` | 列表檢視 | List view |
| `empty` | 沒有媒體檔案 | No media files |
| `colName` | 檔名 | Name |
| `colType` | 類型 | Type |
| `colSize` | 大小 | Size |
| `colDimensions` | 尺寸 | Dimensions |
| `colUploaded` | 上傳於 | Uploaded |
| `detailTitle` | 檔案詳情 | File details |
| `fieldTitle` | 標題 | Title |
| `fieldAlt` | 替代文字 (alt) | Alt text |
| `fileUrl` | 檔案 URL | File URL |
| `copyUrl` | 複製 URL | Copy URL |
| `urlCopied` | 已複製 URL | URL copied |
| `status` | 狀態 | Status |
| `openInEditor` | 在項目表單開啟完整編輯 | Open in full editor |
| `save` | 儲存 | Save |
| `delete` | 刪除檔案 | Delete file |
| `saveConflict` | 此檔案已被他人變更,請重新開啟以取得最新版本。 | This file was changed elsewhere — reopen to get the latest. |
| `dropzone` | 拖放檔案到這裡,或點擊上傳 | Drop files here or click to upload |
| `uploadTitle` | 上傳檔案 | Upload files |
| `loadFailed` | 載入媒體失敗 | Failed to load media |
| `deleteFailed` | 刪除失敗 | Delete failed |
| `saveFailed` | 儲存失敗 | Save failed |

## 5. Data flow (API)

- **List** — `itemsApi.list('file', { page, rows: perPage, sort, search, filter, locale })`.
  - `sort`: `newest` → `-createdAt` (default); `name` → `fileName`.
  - `filter` (type): `image` → `{ contentType: { op: '_starts_with', value: 'image/' } }`;
    `video` → `{ contentType: { op: '_starts_with', value: 'video/' } }`; `all` → no filter.
    (`FilterSpec` in `buildListQuery`; `contentType` is a non-hidden field so `QueryValidator` allows
    it; `_starts_with` = `QueryOperator.StartsWith`.)
  - `locale`: `languageStore.defaultCode || undefined`.
  - Any change to search / type / sort / page resets to `page 0` (except `onPage`) and reloads.
- **Upload** — `filesApi.upload(file)` per file (unchanged).
- **Delete** — `filesApi.remove(id)` (unchanged).
- **Detail load** — `itemsApi.get('file', id)` → `parseItemToForm`.
- **Detail save** — `buildItemPayload(... 'update')` → `itemsApi.update('file', id, payload)`.

## 6. Styling

- Page-head from `PageHeader`; toolbar controls (`Select` × 2, `SelectButton` view toggle, search) use
  the PrimeVue Aura preset + FE-R0 layout tokens. Grid tiles, list rows, dialog panels, dropzone, and
  key/value metadata rows use OKLch layout + status tokens — **not** copied prototype CSS.
- Dark/light flip in lockstep with the tokens; component-scoped styles only, no global bleed.
- Icons via **primeicons** (`pi pi-upload`, `pi pi-copy`, `pi pi-trash`, `pi pi-th-large`,
  `pi pi-bars`, `pi pi-external-link`). No new deps.

## 7. Testing

- **Unit**
  - Type-filter → `FilterSpec` mapping (`all`/`image`/`video` → correct `filter` or none) and sort
    mapping (`newest` → `-createdAt`, `name` → `fileName`) — extract as a tiny pure helper if it keeps
    the view thin, else assert via the view.
  - `locales.test.ts` parity for the new `media` namespace.
  - A small human-readable size formatter (bytes → KB/MB), if introduced.
- **Component**
  - `MediaGrid`: existing picker cases still pass (`select` / `toggle` / `is-selected`); new — a plain
    tile click emits `open` when neither `selectable` nor `multiple`.
  - `MediaFileList`: renders columns for the given `FileRow[]`; row click emits `open`.
  - `MediaDetailDialog`: loads + maps an item (mocked `itemsApi.get`), edits Title/Alt for a locale,
    switching locale swaps the bound values, Save calls `itemsApi.update` with a payload built from the
    model (default-locale + edited-locale content + `version`); 409 shows the conflict message; Delete
    (gated) calls `filesApi.remove` and emits `deleted`; copy URL writes to clipboard (mocked) and
    toasts.
  - `MediaUploadDialog`: opening shows the dropzone; the dropzone's `done` re-emits `done`.
  - `MediaUploadDropzone`: existing upload/error behaviour still passes after the re-skin.
- **View** — `MediaLibraryView.test.ts` rebuilt: initial load, search (debounced) reload, type/sort
  change reload with the right `itemsApi.list` args, view toggle grid↔list, pagination `onPage`, open a
  tile → detail dialog receives the file, delete flow, RBAC gating of Upload/Delete, error banner on a
  failed load.
- **Gate** — `pnpm build` (vue-tsc typecheck) **and** `pnpm test` (vitest) both green. Per the FE
  lesson: vitest strips types → run `pnpm build` too. FE test count expected to grow.

## 8. Live verification (recommended)

Pure-frontend, so no DB gate — but a Playwright MCP smoke on real PG/MinIO backend is done because the
screen depends on real upload / list / update / 409 / delete round-trips:

- Backend `ASPNETCORE_URLS=:5080` (Vite proxy default) on real Postgres + MinIO; bootstrap admin;
  `pnpm dev --host 127.0.0.1`. Use the `plugin_playwright` MCP, logged-in.
- Flow: open `/media`; upload an image (dialog) → appears in grid; search by filename; type filter
  Images/Video/All; sort newest/name; toggle grid↔list; paginate if >24; open a file → detail dialog;
  edit Alt for the default locale, switch locale, edit the other locale's Title, Save → reopen and
  confirm both persisted; copy URL; delete → confirm removed; dark/light lockstep; **0 console errors**.

## 9. Risks / notes

- **`MediaGrid` is shared with `FilePicker`** — the re-skin must not change its `selectable`/`multiple`
  contract. The added `open` emit is additive and inert in picker mode; covered by keeping the existing
  `MediaGrid` picker tests and adding an `open`-emit test. This is the one component touched by two
  consumers — treat it carefully.
- **Per-locale Title/Alt in the dialog** — reuses `parseItemToForm` / `buildItemPayload` (no new
  write-path logic) so it stays consistent with the item form, including the `version` token. The
  dialog deliberately does **not** reimplement the item form's full reload-latest merge; on 409 it
  asks the user to reopen. Status editing is intentionally left to the full form (link out).
- **`itemsApi.get('file', id)` must return the per-locale `translations` map** for per-locale editing to
  work (same assumption the item form relies on). Confirmed by `parseItemToForm` reading
  `item.translations` keyed by locale; verified live in §8.
- **Type filter honesty** — only `image/` and `video/` are offered because they map to a single
  `_starts_with`; "文件"/"other" has no clean single-condition mapping (no `NotStartsWith`). Documented
  so the reduced set is not mistaken for an oversight.
- **Pagination + grid** — page size 24 (grid-friendly). Search/type/sort/page are all server-side and
  consistent; no client-side filtering (which would desync from `meta.total`).
- **No new deps** — reuse PrimeVue (`Dialog`, `Select`, `SelectButton`, `DataTable`, `Paginator`,
  `Button`, `InputText`, `ConfirmDialog`, `Toast`) + primeicons; no `pnpm add`.

## 10. Process

Subagent-driven (impl = Sonnet / review = Opus per task + Opus whole-branch review before merge),
spec → plan → execute → verify with an SDD ledger, `--no-ff` merge to `main`. Matches the FE-R0..R5
cadence. See [[workflow-model-and-cost-prefs]].
