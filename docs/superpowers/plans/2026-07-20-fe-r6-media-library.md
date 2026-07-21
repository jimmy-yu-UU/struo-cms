# FE-R6 Media Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the StruoCMS admin media library (`/media`) on the FE-R0 design system — page-head + toolbar (search / type filter / sort / grid·list toggle) + paginated grid-or-list + a file-detail dialog with per-locale Title/Alt editing + an upload dialog — with no backend, route, or dependency change.

**Architecture:** Pure-frontend re-skin + assembly. The existing `file`-collection API (`itemsApi` for list/get/update, `filesApi` for upload/delete) is reused verbatim; new UI is composed from the shared FE-R0 components (`PageHeader`, `ListToolbar`, `TableFooter`) plus re-skinned/new media components. The detail dialog reuses the item-form's `parseItemToForm` / `buildItemPayload` helpers so the write path (incl. the `version` concurrency token + 409 handling) is identical to the item form.

**Tech Stack:** Vue 3.5 `<script setup lang="ts">`, PrimeVue 4.5 (Aura preset), Pinia, vue-i18n 11, Vitest 4 + @vue/test-utils, pnpm.

## Global Constraints

- **No backend / route / dependency change.** No `dotnet` build. No `pnpm add` — reuse existing PrimeVue components + primeicons only.
- **Design-reference rule:** the prototype (`docs/struo-cms-frontend-design/struocms-admin-prototype.html`) is a *visual* reference only — **copy no code from it**.
- **Honest data:** type filter is **全部 / 圖片 / 影音** only (no "文件" bucket). No publish-status invention.
- **Design tokens:** colours come from `src/assets/theme.css` tokens — `--danger`, `--success`, `--accent`, `--bg`, `--surface`, `--fg`, `--muted`, `--border`, `--radius`, `--shadow-2`. Never hardcode hex (e.g. no `#d33`). Dark/light must flip in lockstep (tokens handle this).
- **i18n:** all user-facing strings go through the new `media` namespace (zh-TW default / en fallback). `src/locales/locales.test.ts` enforces zh-TW/en key parity.
- **Gate:** `pnpm build` (vue-tsc) **and** `pnpm test` (vitest) both green after every task. vitest strips types, so `pnpm build` is a required separate gate.
- **RBAC:** upload/save gated on `auth.canWrite('file')`; delete gated on `auth.canDelete('file')`.
- **Immutability / small files:** new objects over mutation; one responsibility per file.
- **Process:** subagent-driven (impl = Sonnet / review = Opus per task + Opus whole-branch review), `--no-ff` merge to `main`. Branch `fe-r6-media-library` already exists with the spec commit.

**Working directory:** `D:\dotnet\struo-cms\frontend`. All commands below are run from there. Run tests with `pnpm test -- <path>` (vitest) and the typecheck with `pnpm build`.

---

### Task 1: `media` i18n namespace

**Files:**
- Modify: `src/locales/en.ts` (add `media` block after `itemForm`)
- Modify: `src/locales/zh-TW.ts` (add matching `media` block)
- Test: `src/locales/locales.test.ts` (existing parity test — must stay green)

**Interfaces:**
- Produces: the `media.*` message keys consumed by every later task.

- [ ] **Step 1: Add the `media` namespace to `en.ts`**

Insert this block as the last key of the default-exported object in `src/locales/en.ts` (after the `itemForm` block, adding a comma after `itemForm`'s closing brace):

```ts
  media: {
    title: 'Media Library',
    count: '{n} files',
    upload: 'Upload',
    searchPlaceholder: 'Search files…',
    typeAll: 'All types',
    typeImage: 'Images',
    typeVideo: 'Video',
    sortNewest: 'Newest',
    sortName: 'By name',
    viewGrid: 'Grid view',
    viewList: 'List view',
    empty: 'No media files',
    colName: 'Name',
    colType: 'Type',
    colSize: 'Size',
    colDimensions: 'Dimensions',
    colUploaded: 'Uploaded',
    detailTitle: 'File details',
    fieldTitle: 'Title',
    fieldAlt: 'Alt text',
    fileUrl: 'File URL',
    copyUrl: 'Copy URL',
    urlCopied: 'URL copied',
    status: 'Status',
    openInEditor: 'Open in full editor',
    save: 'Save',
    delete: 'Delete file',
    saveConflict: 'This file was changed elsewhere — reopen to get the latest.',
    dropzone: 'Drop files here or click to upload',
    uploadTitle: 'Upload files',
    loadFailed: 'Failed to load media',
    deleteFailed: 'Delete failed',
    saveFailed: 'Save failed',
  },
```

- [ ] **Step 2: Add the matching `media` namespace to `zh-TW.ts`**

Insert this block as the last key of the default-exported object in `src/locales/zh-TW.ts` (mirror the placement — after `itemForm`, with a comma):

```ts
  media: {
    title: '媒體庫',
    count: '{n} 個檔案',
    upload: '上傳檔案',
    searchPlaceholder: '搜尋檔名…',
    typeAll: '全部類型',
    typeImage: '圖片',
    typeVideo: '影音',
    sortNewest: '最新上傳',
    sortName: '依名稱',
    viewGrid: '網格檢視',
    viewList: '列表檢視',
    empty: '沒有媒體檔案',
    colName: '檔名',
    colType: '類型',
    colSize: '大小',
    colDimensions: '尺寸',
    colUploaded: '上傳於',
    detailTitle: '檔案詳情',
    fieldTitle: '標題',
    fieldAlt: '替代文字 (alt)',
    fileUrl: '檔案 URL',
    copyUrl: '複製 URL',
    urlCopied: '已複製 URL',
    status: '狀態',
    openInEditor: '在項目表單開啟完整編輯',
    save: '儲存',
    delete: '刪除檔案',
    saveConflict: '此檔案已被他人變更,請重新開啟以取得最新版本。',
    dropzone: '拖放檔案到這裡,或點擊上傳',
    uploadTitle: '上傳檔案',
    loadFailed: '載入媒體失敗',
    deleteFailed: '刪除失敗',
    saveFailed: '儲存失敗',
  },
```

- [ ] **Step 3: Run the parity test — expect PASS**

Run: `pnpm test -- src/locales/locales.test.ts`
Expected: PASS (zh-TW and en now have identical `media` keys). If it fails with a key-mismatch, a key is missing/typo'd in one file — fix and re-run.

- [ ] **Step 4: Typecheck**

Run: `pnpm build`
Expected: build succeeds (no type errors introduced).

- [ ] **Step 5: Commit**

```bash
git add src/locales/en.ts src/locales/zh-TW.ts
git commit -m "feat(frontend): add media i18n namespace (FE-R6)"
```

---

### Task 2: `formatFileSize` helper

**Files:**
- Create: `src/lib/formatFileSize.ts`
- Test: `src/lib/formatFileSize.test.ts`

**Interfaces:**
- Produces: `formatFileSize(bytes: number): string` — human-readable size ("1.5 KB"), used by `MediaFileList` and `MediaDetailDialog`.

- [ ] **Step 1: Write the failing test**

Create `src/lib/formatFileSize.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { formatFileSize } from './formatFileSize'

describe('formatFileSize', () => {
  it('formats bytes under 1 KiB as B', () => {
    expect(formatFileSize(0)).toBe('0 B')
    expect(formatFileSize(512)).toBe('512 B')
  })
  it('formats KiB with one decimal', () => {
    expect(formatFileSize(1024)).toBe('1.0 KB')
    expect(formatFileSize(1536)).toBe('1.5 KB')
  })
  it('formats MB and GB', () => {
    expect(formatFileSize(1048576)).toBe('1.0 MB')
    expect(formatFileSize(1073741824)).toBe('1.0 GB')
  })
  it('returns empty string for invalid input', () => {
    expect(formatFileSize(-1)).toBe('')
    expect(formatFileSize(Number.NaN)).toBe('')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test -- src/lib/formatFileSize.test.ts`
Expected: FAIL — `formatFileSize` is not defined / module not found.

- [ ] **Step 3: Write the implementation**

Create `src/lib/formatFileSize.ts`:

```ts
const UNITS = ['B', 'KB', 'MB', 'GB', 'TB']

/** Human-readable byte size. Returns '' for negative / non-finite input. */
export function formatFileSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return ''
  if (bytes < 1024) return `${bytes} B`
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < UNITS.length - 1) {
    value /= 1024
    unit += 1
  }
  return `${value.toFixed(1)} ${UNITS[unit]}`
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test -- src/lib/formatFileSize.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/lib/formatFileSize.ts src/lib/formatFileSize.test.ts
git commit -m "feat(frontend): formatFileSize helper (FE-R6)"
```

---

### Task 3: `mediaQuery` helpers (type filter + sort mapping)

**Files:**
- Create: `src/lib/mediaQuery.ts`
- Test: `src/lib/mediaQuery.test.ts`

**Interfaces:**
- Consumes: `FilterSpec` from `src/lib/buildListQuery.ts` (`Record<string, { op: string; value: string }>`).
- Produces:
  - `type MediaType = 'all' | 'image' | 'video'`
  - `type MediaSort = 'newest' | 'name'`
  - `mediaTypeFilter(type: MediaType): FilterSpec | undefined`
  - `mediaSort(sort: MediaSort): string`

- [ ] **Step 1: Write the failing test**

Create `src/lib/mediaQuery.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mediaTypeFilter, mediaSort } from './mediaQuery'

describe('mediaTypeFilter', () => {
  it('returns undefined for all', () => {
    expect(mediaTypeFilter('all')).toBeUndefined()
  })
  it('maps image to a contentType starts-with image/', () => {
    expect(mediaTypeFilter('image')).toEqual({ contentType: { op: '_starts_with', value: 'image/' } })
  })
  it('maps video to a contentType starts-with video/', () => {
    expect(mediaTypeFilter('video')).toEqual({ contentType: { op: '_starts_with', value: 'video/' } })
  })
})

describe('mediaSort', () => {
  it('maps newest to -createdAt', () => {
    expect(mediaSort('newest')).toBe('-createdAt')
  })
  it('maps name to fileName', () => {
    expect(mediaSort('name')).toBe('fileName')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test -- src/lib/mediaQuery.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the implementation**

Create `src/lib/mediaQuery.ts`:

```ts
import type { FilterSpec } from './buildListQuery'

export type MediaType = 'all' | 'image' | 'video'
export type MediaSort = 'newest' | 'name'

// image/ and video/ each map to a single contentType starts-with condition (backend
// QueryOperator.StartsWith, REST token `_starts_with`; contentType is a non-hidden [CmsField]
// so QueryValidator allows it). "Documents" is intentionally omitted — application/* + text/*
// has no clean single-condition mapping and there is no NotStartsWith operator (see spec §0).
export function mediaTypeFilter(type: MediaType): FilterSpec | undefined {
  if (type === 'image') return { contentType: { op: '_starts_with', value: 'image/' } }
  if (type === 'video') return { contentType: { op: '_starts_with', value: 'video/' } }
  return undefined
}

export function mediaSort(sort: MediaSort): string {
  return sort === 'name' ? 'fileName' : '-createdAt'
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test -- src/lib/mediaQuery.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/lib/mediaQuery.ts src/lib/mediaQuery.test.ts
git commit -m "feat(frontend): media type-filter + sort query mapping (FE-R6)"
```

---

### Task 4: Extend `FileRow` + re-skin `FileThumbnail`

**Files:**
- Modify: `src/components/media/FileThumbnail.vue`
- Test: `src/components/media/FileThumbnail.test.ts` (existing — keep green; add a dimensions-agnostic case if none exists)

**Interfaces:**
- Produces: extended `FileRow` type — adds optional `width`, `height`, `status`, `createdAt` (consumed by `MediaFileList` + `MediaDetailDialog`). Existing 4 required fields unchanged so all current consumers (`MediaGrid`, `FilePicker`) still compile.

- [ ] **Step 1: Extend the `FileRow` type**

In `src/components/media/FileThumbnail.vue`, replace the `FileRow` export with:

```ts
export type FileRow = {
  id: string
  fileName: string
  contentType: string
  size: number
  width?: number | null
  height?: number | null
  status?: string
  createdAt?: string
}
```

- [ ] **Step 2: Re-skin the thumbnail styles to design tokens**

Replace the `<style scoped>` block in `FileThumbnail.vue` with token-based styling (keep the existing template + script; only the chip colours/borders move to tokens):

```css
.file-thumb {
  width: 100%;
  height: 120px;
  display: flex;
}
.file-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: var(--radius, 8px);
}
.file-chip {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
  border: 1px solid var(--border);
  border-radius: var(--radius, 8px);
  background: var(--bg);
  padding: 8px;
  overflow: hidden;
}
.file-chip__name {
  font-size: 12px;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--fg);
}
.file-chip__meta {
  font-size: 11px;
  color: var(--muted);
}
```

- [ ] **Step 3: Run the existing thumbnail test — expect PASS**

Run: `pnpm test -- src/components/media/FileThumbnail.test.ts`
Expected: PASS (behaviour unchanged; type is a superset). If the test file does not exist, create a minimal one:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import FileThumbnail from './FileThumbnail.vue'

describe('FileThumbnail', () => {
  it('renders an img for image content types', () => {
    const w = mount(FileThumbnail, { props: { file: { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 } } })
    expect(w.find('img').exists()).toBe(true)
  })
  it('renders a chip for non-image content types', () => {
    const w = mount(FileThumbnail, { props: { file: { id: 'f2', fileName: 'a.pdf', contentType: 'application/pdf', size: 1 } } })
    expect(w.find('.file-chip').exists()).toBe(true)
  })
})
```

- [ ] **Step 4: Typecheck**

Run: `pnpm build`
Expected: build succeeds — confirms all existing `FileRow` consumers still compile with the widened type.

- [ ] **Step 5: Commit**

```bash
git add src/components/media/FileThumbnail.vue src/components/media/FileThumbnail.test.ts
git commit -m "feat(frontend): extend FileRow + re-skin FileThumbnail (FE-R6)"
```

---

### Task 5: `MediaGrid` re-skin + `open` emit

**Files:**
- Modify: `src/components/media/MediaGrid.vue`
- Test: `src/components/media/MediaGrid.test.ts` (existing — keep green; add an `open`-emit case)

**Interfaces:**
- Consumes: `FileRow` (Task 4).
- Produces: `MediaGrid` now emits `(e: 'open', id: string)` on a plain tile click (neither `selectable` nor `multiple`). Picker emits `select` / `toggle` unchanged.

- [ ] **Step 1: Add the failing `open`-emit test**

Append to `src/components/media/MediaGrid.test.ts` (inside the existing `describe`):

```ts
  it('emits open with the id on click when not selectable', async () => {
    const w = mount(MediaGrid, { props: { files } })
    await w.findAll('.media-tile')[0].trigger('click')
    expect(w.emitted('open')?.[0]).toEqual(['f1'])
    expect(w.emitted('select')).toBeUndefined()
    expect(w.emitted('toggle')).toBeUndefined()
  })
```

- [ ] **Step 2: Run it — expect FAIL**

Run: `pnpm test -- src/components/media/MediaGrid.test.ts`
Expected: FAIL — `open` is never emitted (current `onClick` returns without emitting when not selectable).

- [ ] **Step 3: Add the `open` emit + re-skin**

In `src/components/media/MediaGrid.vue`, update the emits declaration and `onClick`:

```ts
const emit = defineEmits<{
  (e: 'select', id: string): void
  (e: 'toggle', id: string): void
  (e: 'open', id: string): void
}>()

function onClick(id: string): void {
  if (props.multiple) { emit('toggle', id); return }
  if (props.selectable) { emit('select', id); return }
  emit('open', id)
}
```

Then re-skin the `<style scoped>` block to design tokens (keep the template + grid structure):

```css
.media-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  gap: 16px;
}
.media-tile {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg, 12px);
  background: var(--surface);
  cursor: pointer;
  font: inherit;
  color: inherit;
  text-align: left;
  overflow: hidden;
  transition: border-color var(--speed, .15s), box-shadow var(--speed, .15s);
}
.media-tile:hover { border-color: var(--accent); box-shadow: var(--shadow-1); }
.media-tile.is-selected { outline: 2px solid var(--accent); outline-offset: -1px; }
.media-tile__name {
  font-size: 13px;
  color: var(--fg);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
```

- [ ] **Step 4: Run the full grid test — expect PASS**

Run: `pnpm test -- src/components/media/MediaGrid.test.ts`
Expected: PASS — the new `open` case passes and all existing picker cases (`select`/`toggle`/`is-selected`) still pass.

- [ ] **Step 5: Typecheck + commit**

Run: `pnpm build` (expect success), then:

```bash
git add src/components/media/MediaGrid.vue src/components/media/MediaGrid.test.ts
git commit -m "feat(frontend): MediaGrid open emit + re-skin (FE-R6)"
```

---

### Task 6: `MediaFileList` (list view mode)

**Files:**
- Create: `src/components/media/MediaFileList.vue`
- Test: `src/components/media/MediaFileList.test.ts`

**Interfaces:**
- Consumes: `FileRow` (Task 4), `formatFileSize` (Task 2), `media.*` i18n (Task 1).
- Produces: `<MediaFileList :files="FileRow[]" @open="(id: string)">` — a semantic table (not PrimeVue DataTable) so columns + thumbnail are fully controllable and testable. Emits `open` on row click.

- [ ] **Step 1: Write the failing test**

Create `src/components/media/MediaFileList.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaFileList from './MediaFileList.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    colName: 'Name', colType: 'Type', colSize: 'Size', colDimensions: 'Dimensions', colUploaded: 'Uploaded',
  } } },
})

const files = [
  { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024, width: 800, height: 600, createdAt: '2026-07-01T00:00:00Z' },
  { id: 'f2', fileName: 'b.pdf', contentType: 'application/pdf', size: 2048 },
]

function mountList() {
  return mount(MediaFileList, { props: { files }, global: { plugins: [i18n] } })
}

describe('MediaFileList', () => {
  it('renders one row per file', () => {
    const w = mountList()
    expect(w.findAll('.media-list__row')).toHaveLength(2)
  })
  it('shows a human-readable size', () => {
    const w = mountList()
    expect(w.text()).toContain('1.0 KB')
  })
  it('emits open with the id on row click', async () => {
    const w = mountList()
    await w.findAll('.media-list__row')[0].trigger('click')
    expect(w.emitted('open')?.[0]).toEqual(['f1'])
  })
})
```

- [ ] **Step 2: Run it — expect FAIL**

Run: `pnpm test -- src/components/media/MediaFileList.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the component**

Create `src/components/media/MediaFileList.vue`:

```vue
<script setup lang="ts">
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'
import { formatFileSize } from '../../lib/formatFileSize'

defineProps<{ files: FileRow[] }>()
const emit = defineEmits<{ (e: 'open', id: string): void }>()

function dims(f: FileRow): string {
  return f.width && f.height ? `${f.width}×${f.height}` : '—'
}
function uploaded(f: FileRow): string {
  return f.createdAt ? new Date(f.createdAt).toLocaleDateString() : '—'
}
</script>

<template>
  <table class="media-list">
    <thead>
      <tr>
        <th class="media-list__thumb-col" aria-hidden="true"></th>
        <th>{{ $t('media.colName') }}</th>
        <th>{{ $t('media.colType') }}</th>
        <th>{{ $t('media.colSize') }}</th>
        <th>{{ $t('media.colDimensions') }}</th>
        <th>{{ $t('media.colUploaded') }}</th>
      </tr>
    </thead>
    <tbody>
      <tr v-for="f in files" :key="f.id" class="media-list__row" @click="emit('open', f.id)">
        <td class="media-list__thumb"><FileThumbnail :file="f" /></td>
        <td class="media-list__name">{{ f.fileName }}</td>
        <td>{{ f.contentType }}</td>
        <td>{{ formatFileSize(f.size) }}</td>
        <td>{{ dims(f) }}</td>
        <td>{{ uploaded(f) }}</td>
      </tr>
    </tbody>
  </table>
</template>

<style scoped>
.media-list {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}
.media-list th {
  text-align: left;
  padding: 8px 12px;
  color: var(--muted);
  font-weight: 600;
  border-bottom: 1px solid var(--border);
}
.media-list__row {
  cursor: pointer;
  transition: background var(--speed, .15s);
}
.media-list__row:hover { background: var(--bg); }
.media-list td {
  padding: 8px 12px;
  border-bottom: 1px solid var(--border);
  color: var(--fg);
  vertical-align: middle;
}
.media-list__thumb-col { width: 64px; }
.media-list__thumb { width: 56px; }
.media-list__thumb :deep(.file-thumb) { height: 44px; width: 56px; }
.media-list__name { font-weight: 500; }
</style>
```

- [ ] **Step 4: Run it — expect PASS**

Run: `pnpm test -- src/components/media/MediaFileList.test.ts`
Expected: PASS.

- [ ] **Step 5: Typecheck + commit**

Run: `pnpm build` (expect success), then:

```bash
git add src/components/media/MediaFileList.vue src/components/media/MediaFileList.test.ts
git commit -m "feat(frontend): MediaFileList list view (FE-R6)"
```

---

### Task 7: Re-skin `MediaUploadDropzone` (danger token + i18n label)

**Files:**
- Modify: `src/components/media/MediaUploadDropzone.vue`
- Test: `src/components/media/MediaUploadDropzone.test.ts` (existing — keep green)

**Interfaces:**
- Produces: same `uploaded` / `done` emits + `uploadFiles` expose; only styling + the label string change.

- [ ] **Step 1: Confirm the existing test still describes the behaviour**

Run: `pnpm test -- src/components/media/MediaUploadDropzone.test.ts`
Expected: PASS (baseline before changes).

- [ ] **Step 2: i18n the drop label**

In `src/components/media/MediaUploadDropzone.vue`, replace the hardcoded label text:

```html
      <span>{{ $t('media.dropzone') }}</span>
```

(Replaces `<span>Drop files here or click to upload</span>`. `$t` is globally available via the app i18n; component tests that mount this in isolation must provide an i18n plugin with the `media.dropzone` key — the existing test may need a one-line i18n plugin added to its `mount` call; if so, add:

```ts
import { createI18n } from 'vue-i18n'
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: { dropzone: 'Drop files here or click to upload' } } } })
```

and pass `global: { plugins: [i18n] }` to each `mount`.)

- [ ] **Step 3: Replace the hardcoded error colour with the danger token**

In the same file's `<style scoped>`, change the error row colour:

```css
.dropzone__rows li.error {
  color: var(--danger);
}
```

Also align the dropzone container border/background with tokens (keep the drag-over accent behaviour):

```css
.dropzone {
  border: 2px dashed var(--border);
  border-radius: var(--radius, 8px);
  padding: 24px;
  text-align: center;
  background: var(--surface);
  transition: border-color var(--speed, .15s), background-color var(--speed, .15s);
}
.dropzone.is-dragging {
  border-color: var(--accent);
  background: var(--bg);
}
```

- [ ] **Step 4: Run the test — expect PASS**

Run: `pnpm test -- src/components/media/MediaUploadDropzone.test.ts`
Expected: PASS (upload/parallel/error-row behaviour unchanged). If it fails only because `$t` is undefined in the isolated mount, add the i18n plugin from Step 2.

- [ ] **Step 5: Typecheck + commit**

Run: `pnpm build` (expect success), then:

```bash
git add src/components/media/MediaUploadDropzone.vue src/components/media/MediaUploadDropzone.test.ts
git commit -m "feat(frontend): re-skin MediaUploadDropzone to design tokens (FE-R6)"
```

---

### Task 8: `MediaUploadDialog` (wrap the dropzone in a dialog)

**Files:**
- Create: `src/components/media/MediaUploadDialog.vue`
- Test: `src/components/media/MediaUploadDialog.test.ts`

**Interfaces:**
- Consumes: `MediaUploadDropzone` (Task 7), `media.uploadTitle` i18n.
- Produces: `<MediaUploadDialog v-model:visible="bool" @done="() => void>` — re-emits the dropzone's `done`.

- [ ] **Step 1: Write the failing test**

Create `src/components/media/MediaUploadDialog.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaUploadDialog from './MediaUploadDialog.vue'
import MediaUploadDropzone from './MediaUploadDropzone.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: { uploadTitle: 'Upload files', dropzone: 'Drop files here or click to upload' } } },
})

// Stub PrimeVue Dialog to a passthrough so the slot renders without teleport.
const DialogStub = { name: 'Dialog', template: '<div><slot /></div>' }

function mountDialog() {
  return mount(MediaUploadDialog, {
    props: { visible: true },
    global: { plugins: [i18n], stubs: { Dialog: DialogStub } },
  })
}

describe('MediaUploadDialog', () => {
  it('renders the dropzone when visible', () => {
    const w = mountDialog()
    expect(w.findComponent(MediaUploadDropzone).exists()).toBe(true)
  })
  it('re-emits done when the dropzone finishes a batch', async () => {
    const w = mountDialog()
    w.findComponent(MediaUploadDropzone).vm.$emit('done')
    await w.vm.$nextTick()
    expect(w.emitted('done')).toBeTruthy()
  })
})
```

- [ ] **Step 2: Run it — expect FAIL**

Run: `pnpm test -- src/components/media/MediaUploadDialog.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the component**

Create `src/components/media/MediaUploadDialog.vue`:

```vue
<script setup lang="ts">
import Dialog from 'primevue/dialog'
import MediaUploadDropzone from './MediaUploadDropzone.vue'

defineProps<{ visible: boolean }>()
const emit = defineEmits<{
  (e: 'update:visible', value: boolean): void
  (e: 'done'): void
}>()
</script>

<template>
  <Dialog
    :visible="visible"
    modal
    :header="$t('media.uploadTitle')"
    :style="{ width: '32rem' }"
    :dismissable-mask="true"
    @update:visible="emit('update:visible', $event)"
  >
    <MediaUploadDropzone @done="emit('done')" />
  </Dialog>
</template>
```

- [ ] **Step 4: Run it — expect PASS**

Run: `pnpm test -- src/components/media/MediaUploadDialog.test.ts`
Expected: PASS.

- [ ] **Step 5: Typecheck + commit**

Run: `pnpm build` (expect success), then:

```bash
git add src/components/media/MediaUploadDialog.vue src/components/media/MediaUploadDialog.test.ts
git commit -m "feat(frontend): MediaUploadDialog (FE-R6)"
```

---

### Task 9: `MediaDetailDialog` (detail + per-locale Title/Alt)

**Files:**
- Create: `src/components/media/MediaDetailDialog.vue`
- Test: `src/components/media/MediaDetailDialog.test.ts`

**Interfaces:**
- Consumes: `FileRow` (Task 4); `itemsApi.get` / `itemsApi.update` (`src/api/itemsApi.ts`); `filesApi.remove` / `filesApi.contentUrl` (`src/api/filesApi.ts`); `parseItemToForm` / `buildItemPayload` (`src/lib/*`); `useSchemaStore` (`schema.get('file')`, `schema.load()`); `useLanguageStore` (`languages`, `defaultCode`, `load()`); `ApiError` (`src/api/apiClient.ts`); `deleteConfirm` (`src/lib/deleteAction.ts`); `formatFileSize` (Task 2); `media.*` i18n.
- Produces: `<MediaDetailDialog :file="FileRow|null" :can-write :can-delete @close @saved @deleted>`.

**Behaviour contract (mirror the item form):**
- On `file` becoming non-null: `schema.load()` + `langStore.load()`, then `itemsApi.get('file', file.id)` → keep the raw item for read-only metadata + `parseItemToForm(fileMeta, item, locales)` for the editable `title`/`alt` model (+ `version`).
- `activeLocale` starts at `langStore.defaultCode`; Title/Alt inputs bind to `model.translations[activeLocale]`.
- Save (gated `canWrite`): `buildItemPayload(fileMeta, model, locales, 'update')` → `itemsApi.update('file', id, payload)`; success → emit `saved` + `close`. On `ApiError` with `status === 409 && code === 'VERSION_CONFLICT'` → set `conflict` message; other errors → `saveFailed`.
- Delete (gated `canDelete`): `confirm.require(deleteConfirm('hard'))` → `filesApi.remove(id)` → emit `deleted`.
- Copy URL: `navigator.clipboard.writeText(filesApi.contentUrl(id))` → toast `urlCopied`.
- "Open in full editor": `router.push({ name: 'collection-item', params: { name: 'file', id } })`.

- [ ] **Step 1: Write the failing test**

Create `src/components/media/MediaDetailDialog.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import MediaDetailDialog from './MediaDetailDialog.vue'
import { itemsApi } from '../../api/itemsApi'
import { filesApi } from '../../api/filesApi'
import { ApiError } from '../../api/apiClient'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
const confirmRequire = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
const toastAdd = vi.fn()
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    detailTitle: 'File details', fieldTitle: 'Title', fieldAlt: 'Alt text', fileUrl: 'File URL',
    copyUrl: 'Copy URL', urlCopied: 'URL copied', status: 'Status', openInEditor: 'Open in full editor',
    save: 'Save', delete: 'Delete file', saveConflict: 'Changed elsewhere', saveFailed: 'Save failed',
    colSize: 'Size', colDimensions: 'Dimensions', colUploaded: 'Uploaded',
  } } },
})

const DialogStub = { name: 'Dialog', template: '<div v-if="visible"><slot /><slot name="footer" /></div>', props: ['visible'] }

const fileMeta = {
  name: 'file', label: 'File',
  fields: [
    { name: 'fileName', label: 'File Name', interface: 'text', required: false, searchable: true, sortable: false, readOnly: true, hidden: false, translatable: false, sort: 1, isSystem: false },
    { name: 'contentType', label: 'Content Type', interface: 'text', required: false, searchable: false, sortable: false, readOnly: true, hidden: false, translatable: false, sort: 2, isSystem: false },
    { name: 'size', label: 'Size', interface: 'number', required: false, searchable: false, sortable: false, readOnly: true, hidden: false, translatable: false, sort: 3, isSystem: false },
    { name: 'status', label: 'Status', interface: 'select', required: false, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: false, sort: 6, isSystem: false },
    { name: 'title', label: 'Title', interface: 'text', required: false, searchable: true, sortable: false, readOnly: false, hidden: false, translatable: true, sort: 1, isSystem: false },
    { name: 'alt', label: 'Alt', interface: 'text', required: false, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: true, sort: 2, isSystem: false },
  ],
  relations: [],
}

function seedStores() {
  const schema = useSchemaStore()
  schema.collections = [fileMeta as never]
  schema.loaded = true
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }, { code: 'zh-TW', name: '繁中', isDefault: false }]
  lang.loaded = true
}

const item = {
  id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024, width: 800, height: 600, status: 'published',
  createdAt: '2026-07-01T00:00:00Z', version: 3,
  translations: { en: { title: 'Hello', alt: 'An image' }, 'zh-TW': { title: '', alt: '' } },
}

function mountDialog() {
  return mount(MediaDetailDialog, {
    props: { file: { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024 }, canWrite: true, canDelete: true },
    global: { plugins: [i18n], stubs: { Dialog: DialogStub, InputText: { name: 'InputText', template: '<input />' }, Button: { name: 'Button', template: '<button><slot /></button>', props: ['label'] }, SelectButton: { name: 'SelectButton', template: '<div />' }, ConfirmDialog: { name: 'ConfirmDialog', template: '<div />' } } },
  })
}

describe('MediaDetailDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    push.mockClear(); confirmRequire.mockClear(); toastAdd.mockClear()
    seedStores()
  })

  it('loads the item and exposes a per-locale model', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    expect(itemsApi.get).toHaveBeenCalledWith('file', 'f1')
    const vm = w.vm as unknown as { model: { translations: Record<string, Record<string, unknown>>; version?: number } }
    expect(vm.model.translations.en.title).toBe('Hello')
    expect(vm.model.version).toBe(3)
  })

  it('saves via itemsApi.update with a payload including version, then emits saved', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({} as never)
    const w = mountDialog()
    await flushPromises()
    await (w.vm as unknown as { onSave: () => Promise<void> }).onSave()
    await flushPromises()
    expect(update).toHaveBeenCalledWith('file', 'f1', expect.objectContaining({ version: 3 }))
    expect(w.emitted('saved')).toBeTruthy()
  })

  it('shows a conflict message on 409 VERSION_CONFLICT and does not emit saved', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    vi.spyOn(itemsApi, 'update').mockRejectedValue(new ApiError(409, 'conflict', 'VERSION_CONFLICT'))
    const w = mountDialog()
    await flushPromises()
    await (w.vm as unknown as { onSave: () => Promise<void> }).onSave()
    await flushPromises()
    expect((w.vm as unknown as { conflict: boolean }).conflict).toBe(true)
    expect(w.emitted('saved')).toBeFalsy()
  })

  it('deletes via filesApi.remove after confirm accept and emits deleted', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const remove = vi.spyOn(filesApi, 'remove').mockResolvedValue()
    const w = mountDialog()
    await flushPromises()
    ;(w.vm as unknown as { onDelete: () => void }).onDelete()
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    const accept = confirmRequire.mock.calls[0][0].accept as () => Promise<void>
    await accept()
    await flushPromises()
    expect(remove).toHaveBeenCalledWith('f1')
    expect(w.emitted('deleted')).toBeTruthy()
  })
})
```

- [ ] **Step 2: Run it — expect FAIL**

Run: `pnpm test -- src/components/media/MediaDetailDialog.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the component**

Create `src/components/media/MediaDetailDialog.vue`:

```vue
<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useRouter } from 'vue-router'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import SelectButton from 'primevue/selectbutton'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useI18n } from 'vue-i18n'
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { filesApi } from '../../api/filesApi'
import { ApiError } from '../../api/apiClient'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import { parseItemToForm } from '../../lib/parseItemToForm'
import { buildItemPayload } from '../../lib/buildItemPayload'
import { deleteConfirm } from '../../lib/deleteAction'
import { formatFileSize } from '../../lib/formatFileSize'
import type { FormModel } from '../../types/itemForm'

const props = defineProps<{ file: FileRow | null; canWrite: boolean; canDelete: boolean }>()
const emit = defineEmits<{ (e: 'close'): void; (e: 'saved'): void; (e: 'deleted'): void }>()

const router = useRouter()
const confirm = useConfirm()
const toast = useToast()
const { t } = useI18n()
const schema = useSchemaStore()
const langStore = useLanguageStore()

const visible = computed(() => props.file !== null)
const raw = ref<Record<string, unknown> | null>(null)
const model = ref<FormModel>({ shared: {}, translations: {}, relations: {} })
const activeLocale = ref('')
const loading = ref(false)
const saving = ref(false)
const conflict = ref(false)
const error = ref('')

const fileMeta = computed(() => schema.get('file'))
const locales = computed(() => langStore.languages)
const localeOptions = computed(() =>
  locales.value.map((l) => ({ label: l.name, value: l.code })),
)

const isImage = computed(() => (props.file?.contentType ?? '').startsWith('image/'))
const previewSrc = computed(() => (props.file ? filesApi.contentUrl(props.file.id) : ''))
const dimensions = computed(() => {
  const w = raw.value?.width as number | undefined
  const h = raw.value?.height as number | undefined
  return w && h ? `${w}×${h}` : '—'
})
const sizeText = computed(() =>
  typeof raw.value?.size === 'number' ? formatFileSize(raw.value.size as number) : '—',
)
const uploadedText = computed(() =>
  typeof raw.value?.createdAt === 'string' ? new Date(raw.value.createdAt as string).toLocaleString() : '—',
)
const statusText = computed(() => (raw.value?.status as string | undefined) ?? '—')
const fileUrl = computed(() => (props.file ? filesApi.contentUrl(props.file.id) : ''))

const activeValues = computed<Record<string, unknown>>({
  get: () => model.value.translations[activeLocale.value] ?? {},
  set: (v) => { model.value = { ...model.value, translations: { ...model.value.translations, [activeLocale.value]: v } } },
})

function setField(name: 'title' | 'alt', value: string): void {
  activeValues.value = { ...activeValues.value, [name]: value }
}

async function load(): Promise<void> {
  if (!props.file) return
  loading.value = true
  conflict.value = false
  error.value = ''
  try {
    await Promise.all([schema.load(), langStore.load()])
    activeLocale.value = langStore.defaultCode
    const item = await itemsApi.get('file', props.file.id)
    raw.value = item
    if (fileMeta.value) model.value = parseItemToForm(fileMeta.value, item, locales.value)
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('media.loadFailed')
  } finally {
    loading.value = false
  }
}

async function onSave(): Promise<void> {
  if (!props.file || !fileMeta.value) return
  saving.value = true
  conflict.value = false
  error.value = ''
  try {
    const payload = buildItemPayload(fileMeta.value, model.value, locales.value, 'update')
    await itemsApi.update('file', props.file.id, payload)
    emit('saved')
    emit('close')
  } catch (e) {
    if (e instanceof ApiError && e.status === 409 && e.code === 'VERSION_CONFLICT') {
      conflict.value = true
    } else {
      error.value = e instanceof Error ? e.message : t('media.saveFailed')
    }
  } finally {
    saving.value = false
  }
}

function onDelete(): void {
  if (!props.file) return
  const id = props.file.id
  confirm.require({
    ...deleteConfirm('hard'),
    accept: async () => {
      try {
        await filesApi.remove(id)
        emit('deleted')
        emit('close')
      } catch (e) {
        error.value = e instanceof Error ? e.message : t('media.deleteFailed')
      }
    },
  })
}

async function onCopyUrl(): Promise<void> {
  try {
    await navigator.clipboard.writeText(fileUrl.value)
    toast.add({ severity: 'success', summary: t('media.urlCopied'), life: 2000 })
  } catch {
    /* clipboard unavailable (e.g. insecure context) — silently ignore */
  }
}

function onOpenEditor(): void {
  if (!props.file) return
  router.push({ name: 'collection-item', params: { name: 'file', id: props.file.id } })
}

watch(() => props.file?.id, (id) => { if (id) void load() }, { immediate: true })

defineExpose({ model, conflict, onSave, onDelete, onCopyUrl, activeLocale, setField })
</script>

<template>
  <Dialog
    :visible="visible"
    modal
    :header="$t('media.detailTitle')"
    :style="{ width: '52rem' }"
    :dismissable-mask="true"
    @update:visible="(v: boolean) => { if (!v) emit('close') }"
  >
    <ConfirmDialog />
    <div v-if="file" class="md-grid">
      <div class="md-preview">
        <img v-if="isImage" :src="previewSrc" :alt="file.fileName" />
        <FileThumbnail v-else :file="file" />
      </div>
      <div class="md-fields">
        <SelectButton
          v-if="localeOptions.length > 1"
          v-model="activeLocale"
          :options="localeOptions"
          option-label="label"
          option-value="value"
          :allow-empty="false"
        />
        <label class="md-field">
          <span>{{ $t('media.fieldTitle') }}</span>
          <InputText :model-value="(activeValues.title as string) ?? ''" @update:model-value="setField('title', $event ?? '')" :disabled="!canWrite" />
        </label>
        <label class="md-field">
          <span>{{ $t('media.fieldAlt') }}</span>
          <InputText :model-value="(activeValues.alt as string) ?? ''" @update:model-value="setField('alt', $event ?? '')" :disabled="!canWrite" />
        </label>

        <div class="md-kv"><span>{{ $t('media.colDimensions') }}</span><b>{{ dimensions }}</b></div>
        <div class="md-kv"><span>{{ $t('media.colSize') }}</span><b>{{ sizeText }}</b></div>
        <div class="md-kv"><span>{{ $t('media.colUploaded') }}</span><b>{{ uploadedText }}</b></div>
        <div class="md-kv"><span>{{ $t('media.status') }}</span><b>{{ statusText }}</b></div>

        <label class="md-field">
          <span>{{ $t('media.fileUrl') }}</span>
          <div class="md-url">
            <InputText :model-value="fileUrl" readonly />
            <Button icon="pi pi-copy" text :aria-label="$t('media.copyUrl')" @click="onCopyUrl" />
          </div>
        </label>

        <p v-if="conflict" class="md-conflict" role="alert">{{ $t('media.saveConflict') }}</p>
        <p v-if="error" class="md-error" role="alert">{{ error }}</p>
      </div>
    </div>

    <template #footer>
      <div class="md-foot">
        <Button
          v-if="canDelete"
          :label="$t('media.delete')"
          icon="pi pi-trash"
          text
          severity="danger"
          @click="onDelete"
        />
        <div class="md-foot__right">
          <Button :label="$t('media.openInEditor')" icon="pi pi-external-link" text severity="secondary" @click="onOpenEditor" />
          <Button v-if="canWrite" :label="$t('media.save')" :loading="saving" @click="onSave" />
        </div>
      </div>
    </template>
  </Dialog>
</template>

<style scoped>
.md-grid { display: grid; grid-template-columns: 240px 1fr; gap: 20px; }
.md-preview {
  border: 1px solid var(--border); border-radius: var(--radius-lg, 12px);
  background: var(--bg); padding: 10px; display: flex; align-items: center; justify-content: center;
  min-height: 200px; overflow: hidden;
}
.md-preview img { max-width: 100%; max-height: 260px; object-fit: contain; border-radius: var(--radius, 8px); }
.md-fields { display: grid; gap: 12px; align-content: start; }
.md-field { display: grid; gap: 4px; }
.md-field > span { font-size: .8rem; color: var(--muted); font-weight: 500; }
.md-kv { display: flex; justify-content: space-between; gap: 12px; font-size: .85rem; }
.md-kv span { color: var(--muted); }
.md-kv b { color: var(--fg); font-weight: 600; }
.md-url { display: flex; gap: 6px; align-items: center; }
.md-url :deep(input) { flex: 1; }
.md-conflict { margin: 0; color: var(--warn); font-size: .85rem; }
.md-error { margin: 0; color: var(--danger); font-size: .85rem; }
.md-foot { display: flex; align-items: center; justify-content: space-between; width: 100%; gap: 10px; }
.md-foot__right { display: flex; gap: 10px; align-items: center; margin-left: auto; }
@media (max-width: 640px) { .md-grid { grid-template-columns: 1fr; } }
</style>
```

- [ ] **Step 4: Run it — expect PASS**

Run: `pnpm test -- src/components/media/MediaDetailDialog.test.ts`
Expected: PASS (all four cases). If the `activeValues` computed setter type trips vue-tsc, ensure the `InputText` `@update:model-value` handlers coerce `$event ?? ''` as shown.

- [ ] **Step 5: Typecheck + commit**

Run: `pnpm build` (expect success), then:

```bash
git add src/components/media/MediaDetailDialog.vue src/components/media/MediaDetailDialog.test.ts
git commit -m "feat(frontend): MediaDetailDialog with per-locale title/alt (FE-R6)"
```

---

### Task 10: Rebuild `MediaLibraryView`

**Files:**
- Modify (rebuild): `src/views/MediaLibraryView.vue`
- Modify (rewrite): `src/views/MediaLibraryView.test.ts`

**Interfaces:**
- Consumes: everything from Tasks 1–9 + `PageHeader`, `ListToolbar`, `TableFooter` (`src/components/common/*`); `itemsApi.list`; `useAuthStore` (`canWrite`/`canDelete`); `useLanguageStore` (`defaultCode`); `debounce`; `createLatestWins`; `mediaTypeFilter`/`mediaSort`.
- Produces: the finished `/media` screen.

- [ ] **Step 1: Rewrite the view test (failing)**

Replace the entire contents of `src/views/MediaLibraryView.test.ts` with:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import MediaLibraryView from './MediaLibraryView.vue'
import MediaUploadDialog from '../components/media/MediaUploadDialog.vue'
import { itemsApi } from '../api/itemsApi'
import { useAuthStore } from '../stores/authStore'
import type { CurrentUser } from '../stores/authStore'

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: vi.fn() }) }))
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: vi.fn() }) }))

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    title: 'Media Library', count: '{n} files', upload: 'Upload', searchPlaceholder: 'Search files…',
    typeAll: 'All types', typeImage: 'Images', typeVideo: 'Video', sortNewest: 'Newest', sortName: 'By name',
    viewGrid: 'Grid view', viewList: 'List view', empty: 'No media files', loadFailed: 'Failed to load media',
  }, collectionList: { range: 'Showing {from}–{to} of {total}' } } },
})

const rows = [{ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1, width: 10, height: 10, createdAt: '2026-07-01T00:00:00Z' }]

const stubs = {
  PageHeader: { name: 'PageHeader', template: '<div><slot name="actions" /></div>' },
  ListToolbar: { name: 'ListToolbar', template: '<div><slot name="filters" /></div>', props: ['searchValue', 'searchPlaceholder'] },
  TableFooter: { name: 'TableFooter', template: '<div />', props: ['first', 'rows', 'total'] },
  Paginator: { name: 'Paginator', template: '<div />' },
  Select: { name: 'Select', template: '<div />' },
  SelectButton: { name: 'SelectButton', template: '<div />' },
  Button: { name: 'Button', template: '<button><slot /></button>', props: ['label'] },
  ConfirmDialog: { name: 'ConfirmDialog', template: '<div />' },
  MediaDetailDialog: { name: 'MediaDetailDialog', template: '<div />', props: ['file', 'canWrite', 'canDelete'] },
}

function seedUser(perms: Partial<Record<'read' | 'write' | 'delete', boolean>>): void {
  const auth = useAuthStore()
  auth.user = { id: 'u1', isSuperAdmin: false, permissions: { file: { read: true, write: false, delete: false, ...perms } } } as CurrentUser
}

function mountView() {
  return mount(MediaLibraryView, { global: { plugins: [i18n], stubs } })
}

describe('MediaLibraryView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('loads files into the grid on mount', async () => {
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mountView()
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('file', expect.objectContaining({ page: 0 }))
    expect(w.findAll('.media-tile')).toHaveLength(1)
  })

  it('applies the image type filter and reloads', async () => {
    const list = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onType: (t: string) => void }).onType('image')
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({
      filter: { contentType: { op: '_starts_with', value: 'image/' } },
    }))
  })

  it('sorts by name', async () => {
    const list = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onSort: (s: string) => void }).onSort('name')
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ sort: 'fileName' }))
  })

  it('opens the detail dialog for a clicked tile', async () => {
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mountView()
    await flushPromises()
    await w.find('.media-tile').trigger('click')
    expect(w.findComponent({ name: 'MediaDetailDialog' }).props('file')).toEqual(rows[0])
  })

  it('reloads once per upload batch (dialog done event)', async () => {
    const list = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mountView()
    await flushPromises()
    expect(list).toHaveBeenCalledTimes(1)
    w.findComponent(MediaUploadDialog).vm.$emit('done')
    await flushPromises()
    expect(list).toHaveBeenCalledTimes(2)
  })

  it('hides Upload for a user without write on file', async () => {
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mountView()
    seedUser({ write: false })
    await flushPromises()
    const labels = w.findAllComponents({ name: 'Button' }).map((b) => b.props('label'))
    expect(labels).not.toContain('Upload')
  })
})
```

- [ ] **Step 2: Run it — expect FAIL**

Run: `pnpm test -- src/views/MediaLibraryView.test.ts`
Expected: FAIL — the current view has no `onType`/`onSort`, no `MediaUploadDialog`, no `MediaDetailDialog`, no i18n `media.title`.

- [ ] **Step 3: Rebuild the view**

Replace the entire contents of `src/views/MediaLibraryView.vue` with:

```vue
<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import Select from 'primevue/select'
import SelectButton from 'primevue/selectbutton'
import Paginator from 'primevue/paginator'
import PageHeader from '../components/common/PageHeader.vue'
import ListToolbar from '../components/common/ListToolbar.vue'
import TableFooter from '../components/common/TableFooter.vue'
import MediaGrid from '../components/media/MediaGrid.vue'
import MediaFileList from '../components/media/MediaFileList.vue'
import MediaUploadDialog from '../components/media/MediaUploadDialog.vue'
import MediaDetailDialog from '../components/media/MediaDetailDialog.vue'
import type { FileRow } from '../components/media/FileThumbnail.vue'
import { itemsApi } from '../api/itemsApi'
import { useAuthStore } from '../stores/authStore'
import { useLanguageStore } from '../stores/languageStore'
import { debounce } from '../lib/debounce'
import { createLatestWins } from '../lib/latestWins'
import { mediaTypeFilter, mediaSort, type MediaType, type MediaSort } from '../lib/mediaQuery'

const { t } = useI18n()
const auth = useAuthStore()
const langStore = useLanguageStore()

const files = ref<FileRow[]>([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const page = ref(0)
const perPage = ref(24)
const search = ref('')
const type = ref<MediaType>('all')
const sort = ref<MediaSort>('newest')
const view = ref<'grid' | 'list'>('grid')
const selected = ref<FileRow | null>(null)
const uploadOpen = ref(false)

const canWrite = computed(() => auth.canWrite('file'))
const canDelete = computed(() => auth.canDelete('file'))

const typeOptions = computed(() => [
  { label: t('media.typeAll'), value: 'all' as const },
  { label: t('media.typeImage'), value: 'image' as const },
  { label: t('media.typeVideo'), value: 'video' as const },
])
const sortOptions = computed(() => [
  { label: t('media.sortNewest'), value: 'newest' as const },
  { label: t('media.sortName'), value: 'name' as const },
])
const viewOptions = computed(() => [
  { label: t('media.viewGrid'), value: 'grid' as const, icon: 'pi pi-th-large' },
  { label: t('media.viewList'), value: 'list' as const, icon: 'pi pi-bars' },
])

const mediaLoad = createLatestWins()

async function load(): Promise<void> {
  const token = mediaLoad.next()
  loading.value = true
  error.value = ''
  try {
    await langStore.load()
    const res = await itemsApi.list('file', {
      page: page.value,
      rows: perPage.value,
      sort: mediaSort(sort.value),
      search: search.value || undefined,
      filter: mediaTypeFilter(type.value),
      locale: langStore.defaultCode || undefined,
    })
    if (!mediaLoad.isCurrent(token)) return
    files.value = res.data as unknown as FileRow[]
    total.value = res.total
  } catch (e) {
    if (!mediaLoad.isCurrent(token)) return
    error.value = e instanceof Error ? e.message : t('media.loadFailed')
    files.value = []
    total.value = 0
  } finally {
    if (mediaLoad.isCurrent(token)) loading.value = false
  }
}

function reload(): void {
  page.value = 0
  load()
}

const debouncedSearch = debounce(() => { page.value = 0; load() }, 300)
function onSearchInput(value: string): void {
  search.value = value
  debouncedSearch()
}
function onType(value: MediaType): void { type.value = value; reload() }
function onSort(value: MediaSort): void { sort.value = value; reload() }
function onPage(e: { page: number; rows: number }): void {
  page.value = e.page
  perPage.value = e.rows
  load()
}
function openDetail(id: string): void {
  selected.value = files.value.find((f) => f.id === id) ?? null
}
function onDeleted(): void { selected.value = null; load() }

onMounted(load)
onUnmounted(() => debouncedSearch.cancel())

defineExpose({ load, reload, onType, onSort, onPage, onSearchInput, openDetail, onDeleted,
  files, total, loading, error, canWrite, canDelete, selected })
</script>

<template>
  <section class="media-library">
    <PageHeader :title="t('media.title')" :caption="t('media.count', { n: total })">
      <template #actions>
        <Button v-if="canWrite" :label="t('media.upload')" icon="pi pi-upload" @click="uploadOpen = true" />
      </template>
    </PageHeader>

    <ListToolbar :search-value="search" :search-placeholder="t('media.searchPlaceholder')" @search="onSearchInput">
      <template #filters>
        <Select :model-value="type" :options="typeOptions" option-label="label" option-value="value"
                @update:model-value="onType" />
        <Select :model-value="sort" :options="sortOptions" option-label="label" option-value="value"
                @update:model-value="onSort" />
        <SelectButton v-model="view" :options="viewOptions" option-label="label" option-value="value"
                      :allow-empty="false">
          <template #option="{ option }"><i :class="option.icon" :aria-label="option.label" /></template>
        </SelectButton>
      </template>
    </ListToolbar>

    <p v-if="error" class="error" role="alert">{{ error }}</p>

    <MediaGrid v-if="view === 'grid'" :files="files" @open="openDetail" />
    <MediaFileList v-else :files="files" @open="openDetail" />
    <p v-if="!loading && !files.length" class="empty">{{ t('media.empty') }}</p>

    <div v-if="total > perPage" class="media-foot">
      <TableFooter :first="page * perPage" :rows="perPage" :total="total" />
      <Paginator :rows="perPage" :total-records="total" :first="page * perPage" @page="onPage" />
    </div>

    <MediaUploadDialog v-model:visible="uploadOpen" @done="reload" />
    <MediaDetailDialog :file="selected" :can-write="canWrite" :can-delete="canDelete"
                       @close="selected = null" @saved="load" @deleted="onDeleted" />
  </section>
</template>

<style scoped>
.media-library { display: block; }
.error { color: var(--danger); font-size: 0.9rem; margin: 0 0 12px; }
.empty { color: var(--muted); text-align: center; padding: 40px 0; }
.media-foot {
  display: flex; align-items: center; justify-content: space-between; gap: 12px;
  margin-top: 16px; flex-wrap: wrap;
}
</style>
```

- [ ] **Step 4: Run the view test — expect PASS**

Run: `pnpm test -- src/views/MediaLibraryView.test.ts`
Expected: PASS (all six cases). Note the `Select` stub emits nothing, so the tests drive `onType`/`onSort` directly via `defineExpose` — the wiring in the template (`@update:model-value="onType"`) is what production uses.

- [ ] **Step 5: Full suite + typecheck**

Run: `pnpm test` (whole suite) then `pnpm build`.
Expected: all tests green; build clean. Fix any fallout (e.g. a stale import) before committing.

- [ ] **Step 6: Commit**

```bash
git add src/views/MediaLibraryView.vue src/views/MediaLibraryView.test.ts
git commit -m "feat(frontend): rebuild MediaLibraryView on FE-R0 design system (FE-R6)"
```

---

## Verification (after all tasks)

- [ ] **Whole-suite gate:** `pnpm test` green, `pnpm build` clean.
- [ ] **Whole-branch review** (Opus) against the spec — Ready-to-merge gate.
- [ ] **Live Playwright smoke** (recommended, not a hard gate) — real PG + MinIO backend:
  - Start backend `ASPNETCORE_URLS=:5080` (Development) on real Postgres + MinIO; bootstrap admin; `pnpm dev --host 127.0.0.1`. Use the `plugin_playwright` MCP, logged-in.
  - Flow: open `/media`; upload an image via the dialog → appears in the grid; search by filename; type filter Images / Video / All; sort Newest / By name; toggle grid ↔ list; paginate if > 24; click a file → detail dialog; edit Alt for the default locale, switch locale, edit the other locale's Title, Save → reopen and confirm both persisted; copy URL (toast); delete → confirm removed; dark/light lockstep; **0 console errors**.
- [ ] **Merge:** `--no-ff` into `main` once review + smoke pass.

## Self-Review notes (author)

- **Spec coverage:** page-head + upload (T10/T8), toolbar search/type/sort/view (T10 + T3), grid re-skin + open (T5), list view (T6), pagination (T10), detail dialog w/ per-locale title/alt + metadata + copy URL + delete + open-in-editor + 409 (T9), upload dialog (T8), dropzone re-skin (T7), FileRow/thumbnail (T4), `media` i18n (T1), honest type filter (T3). All spec §1 in-scope items map to a task.
- **Type consistency:** `FileRow` widened once in T4 and used everywhere after; `MediaType`/`MediaSort` defined in T3 and imported by T10; `FormModel` reused from `types/itemForm.ts`; `onType`/`onSort`/`openDetail`/`onDeleted` names match between the view template, `defineExpose`, and the tests.
- **No placeholders:** every code + test step contains full content; commands have expected outcomes.
```
