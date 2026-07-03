# Phase 7e — Media Library + File/Image field pickers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the `File` and `Image` field interfaces a real experience: a dedicated Media Library view (browse + drag-drop bulk upload + manage) and a select-only File/Image field picker used inside every other collection's form.

**Architecture:** Frontend-only. Reuse the existing `File` CMS collection (`GET/PUT /api/items/file`) for listing/metadata and the existing `POST /api/files` / `DELETE /api/files/{id}` / `GET /api/files/{id}/content` endpoints for upload/delete/thumbnails. A shared `MediaGrid` is the seam between the library view and the picker dialog. Field values are a plain `Guid?` (identical to SEO's `SeoOgImageId`), so the form save path is unchanged.

**Tech Stack:** Vue 3 + TypeScript (`<script setup>`), PrimeVue (Dialog, Button, InputText), Pinia, Vue Router, Vitest + @vue/test-utils. Backend: .NET 10 sample entity only (one field).

## Global Constraints

- Outbound/inbound JSON is **camelCase** (field value key is `heroImageId`).
- Frontend never hand-authors package versions; use `pnpm add` if a dependency is genuinely needed (none is expected — no new deps).
- No `console.log` in production code (project hook enforces).
- Immutable updates only (spread, never mutate props/store state in place).
- All network calls go through `apiClient` (inherits `credentials: 'include'`, 401 handler, `{ data }` / `{ error: { message } }` envelope).
- `File` is a `[CmsCollection("File", Group = "System", DefaultDisplayField = FileName)]`; its per-locale `Title`/`Alt` live on `FileTranslation`.
- Backend file endpoints: `POST /api/files` (multipart, part name `file`) → `201 { data: { id, fileName, contentType, size, width, height, status } }`; `GET /api/files/{id}` (published only); `GET /api/files/{id}/content` → `302` presigned; `DELETE /api/files/{id}` → `204/404`. Upload + delete require auth.
- Spec: `docs/superpowers/specs/2026-07-03-phase7e-media-library-file-pickers-design.md`.

---

### Task 1: `fieldInputKind` — add `file` / `image` kinds

**Files:**
- Modify: `frontend/src/lib/fieldInputKind.ts`
- Test: `frontend/src/lib/fieldInputKind.test.ts` (create if absent; else append)

**Interfaces:**
- Produces: `InputKind` union gains `'file' | 'image'`; `fieldInputKind('file') === 'file'`, `fieldInputKind('image') === 'image'`. Consumed by Task 8 (`FieldInput.vue`).

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect } from 'vitest'
import { fieldInputKind } from './fieldInputKind'

describe('fieldInputKind file/image', () => {
  it('maps file to file', () => expect(fieldInputKind('file')).toBe('file'))
  it('maps image to image', () => expect(fieldInputKind('image')).toBe('image'))
  it('still falls back to readonly for unknown', () => expect(fieldInputKind('nope')).toBe('readonly'))
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test fieldInputKind`
Expected: FAIL — `file`/`image` currently map to `readonly`.

- [ ] **Step 3: Implement**

In `frontend/src/lib/fieldInputKind.ts`, extend the union and the map:

```ts
export type InputKind =
  | 'text' | 'textarea' | 'richtext' | 'number' | 'boolean'
  | 'date' | 'time' | 'datetime' | 'select' | 'radio' | 'divider'
  | 'file' | 'image' | 'readonly'
```

Add to `MAP`:

```ts
  file: 'file', image: 'image',
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test fieldInputKind`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/fieldInputKind.ts frontend/src/lib/fieldInputKind.test.ts
git commit -m "feat(frontend): fieldInputKind maps file/image interfaces"
```

---

### Task 2: `ApiClient.postForm` + `filesApi`

**Files:**
- Modify: `frontend/src/api/apiClient.ts`
- Create: `frontend/src/api/filesApi.ts`
- Test: `frontend/src/api/filesApi.test.ts`

**Interfaces:**
- Produces:
  - `ApiClient.postForm<T>(path: string, form: FormData): Promise<T>` — multipart POST, no JSON Content-Type; unwraps `{ data }`.
  - `filesApi.upload(file: File): Promise<FileMeta>` where `FileMeta = { id: string; fileName: string; contentType: string; size: number; width: number | null; height: number | null; status: string }`.
  - `filesApi.remove(id: string): Promise<void>`.
  - `filesApi.contentUrl(id: string): string` → `${API_BASE}/files/${id}/content`.
- Consumed by Tasks 3 (contentUrl), 5 (upload), 6 (remove).

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { filesApi } from './filesApi'
import { apiClient } from './apiClient'

describe('filesApi', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('upload posts multipart with part name "file" and returns metadata', async () => {
    const spy = vi.spyOn(apiClient, 'postForm').mockResolvedValue({
      id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 10, width: 2, height: 2, status: 'published',
    } as never)
    const file = new File(['x'], 'a.png', { type: 'image/png' })
    const meta = await filesApi.upload(file)
    expect(meta.id).toBe('f1')
    const [path, form] = spy.mock.calls[0]
    expect(path).toBe('/files')
    expect((form as FormData).get('file')).toBe(file)
  })

  it('remove deletes by id', async () => {
    const spy = vi.spyOn(apiClient, 'delete').mockResolvedValue(undefined as never)
    await filesApi.remove('f1')
    expect(spy).toHaveBeenCalledWith('/files/f1')
  })

  it('contentUrl builds the content path', () => {
    expect(filesApi.contentUrl('f1')).toMatch(/\/files\/f1\/content$/)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test filesApi`
Expected: FAIL — `filesApi` and `apiClient.postForm` do not exist.

- [ ] **Step 3: Implement — extend `ApiClient`**

In `frontend/src/api/apiClient.ts`, add the `postForm` method and make `request` FormData-aware. Add `postForm`:

```ts
  postForm<T>(path: string, form: FormData): Promise<T> {
    return this.request<T>('POST', path, form)
  }
```

And in `request`, detect FormData so the browser sets the multipart boundary (do not JSON-stringify, do not set Content-Type):

```ts
    const isForm = body instanceof FormData
    const res = await fetch(`${this.baseUrl}${path}`, {
      method,
      credentials: 'include',
      headers: body === undefined || isForm ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : isForm ? (body as FormData) : JSON.stringify(body),
    })
```

- [ ] **Step 4: Implement — `filesApi.ts`**

```ts
import { apiClient } from './apiClient'

const API_BASE = import.meta.env.VITE_API_BASE_URL || '/api'

export type FileMeta = {
  id: string
  fileName: string
  contentType: string
  size: number
  width: number | null
  height: number | null
  status: string
}

export const filesApi = {
  async upload(file: File): Promise<FileMeta> {
    const form = new FormData()
    form.append('file', file)
    return apiClient.postForm<FileMeta>('/files', form)
  },
  async remove(id: string): Promise<void> {
    await apiClient.delete<void>(`/files/${id}`)
  },
  contentUrl(id: string): string {
    return `${API_BASE}/files/${id}/content`
  },
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd frontend && pnpm test filesApi`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/apiClient.ts frontend/src/api/filesApi.ts frontend/src/api/filesApi.test.ts
git commit -m "feat(frontend): filesApi (multipart upload/delete/contentUrl) + ApiClient.postForm"
```

---

### Task 3: `FileThumbnail.vue`

**Files:**
- Create: `frontend/src/components/media/FileThumbnail.vue`
- Test: `frontend/src/components/media/FileThumbnail.test.ts`

**Interfaces:**
- Produces: `FileThumbnail` — props `{ file: FileRow }` where `FileRow = { id: string; fileName: string; contentType: string; size: number }` (exported from this SFC). Renders an `<img>` (src = `filesApi.contentUrl(file.id)`) when `contentType` starts with `image/`, else a type/size chip. On `<img>` error, swaps to the chip. Consumed by Tasks 4, 7.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import FileThumbnail from './FileThumbnail.vue'

const img = { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024 }
const doc = { id: 'f2', fileName: 'a.pdf', contentType: 'application/pdf', size: 2048 }

describe('FileThumbnail', () => {
  it('renders an img for image content types', () => {
    const w = mount(FileThumbnail, { props: { file: img } })
    const el = w.find('img')
    expect(el.exists()).toBe(true)
    expect(el.attributes('src')).toMatch(/\/files\/f1\/content$/)
  })

  it('renders a chip (no img) for non-image content types', () => {
    const w = mount(FileThumbnail, { props: { file: doc } })
    expect(w.find('img').exists()).toBe(false)
    expect(w.text()).toContain('a.pdf')
  })

  it('falls back to chip when the image fails to load', async () => {
    const w = mount(FileThumbnail, { props: { file: img } })
    await w.find('img').trigger('error')
    expect(w.find('img').exists()).toBe(false)
    expect(w.text()).toContain('a.png')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test FileThumbnail`
Expected: FAIL — component does not exist.

- [ ] **Step 3: Implement**

```vue
<script setup lang="ts">
import { ref, computed } from 'vue'
import { filesApi } from '../../api/filesApi'

export type FileRow = { id: string; fileName: string; contentType: string; size: number }

const props = defineProps<{ file: FileRow }>()
const broken = ref(false)
const isImage = computed(() => props.file.contentType.startsWith('image/') && !broken.value)
const src = computed(() => filesApi.contentUrl(props.file.id))
</script>

<template>
  <div class="file-thumb">
    <img v-if="isImage" :src="src" :alt="file.fileName" loading="lazy" @error="broken = true" />
    <div v-else class="file-chip">
      <span class="file-chip__name">{{ file.fileName }}</span>
      <span class="file-chip__meta">{{ file.contentType }}</span>
    </div>
  </div>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test FileThumbnail`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/media/FileThumbnail.vue frontend/src/components/media/FileThumbnail.test.ts
git commit -m "feat(frontend): FileThumbnail (image preview or type chip, error fallback)"
```

---

### Task 4: `MediaGrid.vue`

**Files:**
- Create: `frontend/src/components/media/MediaGrid.vue`
- Test: `frontend/src/components/media/MediaGrid.test.ts`

**Interfaces:**
- Consumes: `FileThumbnail` + its `FileRow` type (Task 3).
- Produces: `MediaGrid` — props `{ files: FileRow[]; selectable?: boolean; selectedId?: string | null }`; emits `(e: 'select', id: string)` when a tile is clicked (only when `selectable`). Renders one `FileThumbnail` per file inside a `.media-tile` button; marks the tile matching `selectedId` with `is-selected`. Consumed by Tasks 6, 7.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import MediaGrid from './MediaGrid.vue'

const files = [
  { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 },
  { id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2 },
]

describe('MediaGrid', () => {
  it('renders one tile per file', () => {
    const w = mount(MediaGrid, { props: { files } })
    expect(w.findAll('.media-tile')).toHaveLength(2)
  })

  it('emits select with the file id on click when selectable', async () => {
    const w = mount(MediaGrid, { props: { files, selectable: true } })
    await w.findAll('.media-tile')[1].trigger('click')
    expect(w.emitted('select')?.[0]).toEqual(['f2'])
  })

  it('marks the selected tile', () => {
    const w = mount(MediaGrid, { props: { files, selectable: true, selectedId: 'f2' } })
    expect(w.findAll('.media-tile')[1].classes()).toContain('is-selected')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test MediaGrid`
Expected: FAIL — component does not exist.

- [ ] **Step 3: Implement**

```vue
<script setup lang="ts">
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'

const props = defineProps<{ files: FileRow[]; selectable?: boolean; selectedId?: string | null }>()
const emit = defineEmits<{ (e: 'select', id: string): void }>()

function onClick(id: string): void {
  if (props.selectable) emit('select', id)
}
</script>

<template>
  <div class="media-grid">
    <button
      v-for="f in files"
      :key="f.id"
      type="button"
      class="media-tile"
      :class="{ 'is-selected': selectable && selectedId === f.id }"
      @click="onClick(f.id)"
    >
      <FileThumbnail :file="f" />
      <span class="media-tile__name">{{ f.fileName }}</span>
    </button>
  </div>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test MediaGrid`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/media/MediaGrid.vue frontend/src/components/media/MediaGrid.test.ts
git commit -m "feat(frontend): MediaGrid (thumbnail grid, selectable, shared by library + picker)"
```

---

### Task 5: `MediaUploadDropzone.vue`

**Files:**
- Create: `frontend/src/components/media/MediaUploadDropzone.vue`
- Test: `frontend/src/components/media/MediaUploadDropzone.test.ts`

**Interfaces:**
- Consumes: `filesApi.upload` + `FileMeta` (Task 2).
- Produces: `MediaUploadDropzone` — no props; owns per-file upload state; calls `filesApi.upload(file)` for each selected/dropped file **independently** (one failure does not abort siblings). Emits `(e: 'uploaded', meta: FileMeta)` per success and `(e: 'done')` when the current batch settles. Exposes `uploadFiles(files: File[])` via `defineExpose`. Consumed by Task 6.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import MediaUploadDropzone from './MediaUploadDropzone.vue'
import { filesApi } from '../../api/filesApi'

const meta = (id: string) => ({ id, fileName: id, contentType: 'image/png', size: 1, width: 1, height: 1, status: 'published' })

describe('MediaUploadDropzone', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('uploads each file and emits uploaded per success', async () => {
    vi.spyOn(filesApi, 'upload').mockImplementation((f) => Promise.resolve(meta((f as File).name)))
    const w = mount(MediaUploadDropzone)
    await (w.vm as unknown as { uploadFiles: (f: File[]) => Promise<void> }).uploadFiles([
      new File(['a'], 'a', { type: 'image/png' }),
      new File(['b'], 'b', { type: 'image/png' }),
    ])
    expect(w.emitted('uploaded')).toHaveLength(2)
    expect(w.emitted('done')).toHaveLength(1)
  })

  it('isolates a failing upload and still emits done', async () => {
    vi.spyOn(filesApi, 'upload').mockImplementation((f) =>
      (f as File).name === 'bad' ? Promise.reject(new Error('too big')) : Promise.resolve(meta((f as File).name)))
    const w = mount(MediaUploadDropzone)
    await (w.vm as unknown as { uploadFiles: (f: File[]) => Promise<void> }).uploadFiles([
      new File(['a'], 'ok', { type: 'image/png' }),
      new File(['b'], 'bad', { type: 'image/png' }),
    ])
    expect(w.emitted('uploaded')).toHaveLength(1)
    expect(w.text()).toContain('too big')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test MediaUploadDropzone`
Expected: FAIL — component does not exist.

- [ ] **Step 3: Implement**

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { filesApi, type FileMeta } from '../../api/filesApi'

const emit = defineEmits<{ (e: 'uploaded', meta: FileMeta): void; (e: 'done'): void }>()

type Row = { name: string; state: 'uploading' | 'error'; error?: string }
const rows = ref<Row[]>([])
const dragging = ref(false)

async function uploadFiles(files: File[]): Promise<void> {
  await Promise.all(
    files.map(async (file) => {
      const row: Row = { name: file.name, state: 'uploading' }
      rows.value = [...rows.value, row]
      try {
        const meta = await filesApi.upload(file)
        rows.value = rows.value.filter((r) => r !== row)
        emit('uploaded', meta)
      } catch (e) {
        row.state = 'error'
        row.error = e instanceof Error ? e.message : 'Upload failed.'
        rows.value = [...rows.value] // trigger reactivity
      }
    }),
  )
  emit('done')
}

function onInput(e: Event): void {
  const input = e.target as HTMLInputElement
  if (input.files) void uploadFiles(Array.from(input.files))
  input.value = ''
}

function onDrop(e: DragEvent): void {
  dragging.value = false
  if (e.dataTransfer?.files) void uploadFiles(Array.from(e.dataTransfer.files))
}

defineExpose({ uploadFiles })
</script>

<template>
  <div
    class="dropzone"
    :class="{ 'is-dragging': dragging }"
    @dragover.prevent="dragging = true"
    @dragleave.prevent="dragging = false"
    @drop.prevent="onDrop"
  >
    <label class="dropzone__label">
      <span>Drop files here or click to upload</span>
      <input type="file" multiple class="dropzone__input" @change="onInput" />
    </label>
    <ul v-if="rows.length" class="dropzone__rows">
      <li v-for="(r, i) in rows" :key="i" :class="r.state">
        {{ r.name }}<template v-if="r.error"> — {{ r.error }}</template>
      </li>
    </ul>
  </div>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test MediaUploadDropzone`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/media/MediaUploadDropzone.vue frontend/src/components/media/MediaUploadDropzone.test.ts
git commit -m "feat(frontend): MediaUploadDropzone (parallel per-file upload, isolated errors)"
```

---

### Task 6: `MediaLibraryView.vue` + route + nav entry

**Files:**
- Create: `frontend/src/views/MediaLibraryView.vue`
- Modify: `frontend/src/router/index.ts` (add `media` route)
- Modify: `frontend/src/lib/buildNav.ts` (suppress `file` collection from auto nav)
- Modify: `frontend/src/components/CollectionNav.vue` (prepend a static "Media Library" entry)
- Test: `frontend/src/views/MediaLibraryView.test.ts`; append to `frontend/src/lib/buildNav.test.ts`

**Interfaces:**
- Consumes: `itemsApi.list('file', …)`, `MediaGrid` (Task 4), `MediaUploadDropzone` (Task 5), `FileRow` (Task 3), `filesApi.remove` (Task 2).
- Produces: route `{ path: 'media', name: 'media', component: MediaLibraryView }`; `buildNav` never emits an item with `name === 'file'`.

- [ ] **Step 1: Write the failing test (buildNav suppression)**

Append to `frontend/src/lib/buildNav.test.ts`:

```ts
it('suppresses the file collection from auto nav', () => {
  const cols = [
    { name: 'file', label: 'File', group: 'System' },
    { name: 'article', label: 'Article', group: 'Content' },
  ] as never
  const groups = buildNav(cols, true, {})
  const names = groups.flatMap((g) => g.items.map((i) => i.name))
  expect(names).toContain('article')
  expect(names).not.toContain('file')
})
```

- [ ] **Step 2: Write the failing test (view lists + deletes)**

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import MediaLibraryView from './MediaLibraryView.vue'
import { itemsApi } from '../api/itemsApi'
import { filesApi } from '../api/filesApi'

const rows = [{ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }]

describe('MediaLibraryView', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('loads files into the grid on mount', async () => {
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mount(MediaLibraryView, { global: { stubs: { RouterLink: true } } })
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('file', expect.objectContaining({ page: 0 }))
    expect(w.findAll('.media-tile')).toHaveLength(1)
  })

  it('removes a file then refreshes', async () => {
    const list = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const del = vi.spyOn(filesApi, 'remove').mockResolvedValue()
    const w = mount(MediaLibraryView, { global: { stubs: { RouterLink: true } } })
    await flushPromises()
    await (w.vm as unknown as { onDelete: (id: string) => Promise<void> }).onDelete('f1')
    expect(del).toHaveBeenCalledWith('f1')
    expect(list).toHaveBeenCalledTimes(2)
  })
})
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `cd frontend && pnpm test buildNav MediaLibraryView`
Expected: FAIL — suppression not implemented; view does not exist.

- [ ] **Step 4: Implement — `buildNav` suppression**

In `frontend/src/lib/buildNav.ts`, filter out the `file` collection before grouping:

```ts
  const readable = collections.filter(
    (c) => c.name !== 'file' && (isSuperAdmin || permissions[c.name]?.read === true),
  )
```

- [ ] **Step 5: Implement — route**

In `frontend/src/router/index.ts`, import the view and add a child route inside the `AppShell` children:

```ts
import MediaLibraryView from '../views/MediaLibraryView.vue'
```

```ts
        { path: 'media', name: 'media', component: MediaLibraryView },
```

- [ ] **Step 6: Implement — nav entry**

In `frontend/src/components/CollectionNav.vue`, prepend a static Media Library group to `model`:

```ts
const model = computed(() => [
  {
    key: 'media',
    label: 'Media',
    items: [{ key: 'media', label: 'Media Library', command: () => router.push({ name: 'media' }) }],
  },
  ...buildNav(schema.collections, auth.user?.isSuperAdmin ?? false, auth.user?.permissions ?? {}).map(
    (g) => ({
      key: g.group,
      label: g.group,
      items: g.items.map((it) => ({
        key: it.name,
        label: it.label,
        command: () => router.push({ name: 'collection-list', params: { name: it.name } }),
      })),
    }),
  ),
])
```

- [ ] **Step 7: Implement — `MediaLibraryView.vue`**

```vue
<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import MediaGrid from '../components/media/MediaGrid.vue'
import MediaUploadDropzone from '../components/media/MediaUploadDropzone.vue'
import type { FileRow } from '../components/media/FileThumbnail.vue'
import { itemsApi } from '../api/itemsApi'
import { filesApi } from '../api/filesApi'
import { useLanguageStore } from '../stores/languageStore'

const router = useRouter()
const langStore = useLanguageStore()
const files = ref<FileRow[]>([])
const loading = ref(false)
const error = ref('')

async function load(): Promise<void> {
  loading.value = true
  error.value = ''
  try {
    const res = await itemsApi.list('file', { page: 0, rows: 50, locale: langStore.defaultCode || undefined })
    files.value = res.data as unknown as FileRow[]
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load media.'
  } finally {
    loading.value = false
  }
}

async function onDelete(id: string): Promise<void> {
  try {
    await filesApi.remove(id)
    await load()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Delete failed.'
  }
}

function onEdit(id: string): void {
  void router.push({ name: 'collection-item', params: { name: 'file', id } })
}

onMounted(load)
defineExpose({ load, onDelete, onEdit })
</script>

<template>
  <section class="media-library">
    <h1>Media Library</h1>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <MediaUploadDropzone @uploaded="load" />
    <MediaGrid :files="files" />
    <div v-if="files.length" class="media-actions">
      <template v-for="f in files" :key="f.id">
        <Button label="Edit" text size="small" @click="onEdit(f.id)" />
        <Button label="Delete" text severity="danger" size="small" @click="onDelete(f.id)" />
      </template>
    </div>
  </section>
</template>
```

> Listing is unsorted (default). If the live gate wants newest-first, add `sort: '-createdAt'` to the `itemsApi.list` options only after confirming `createdAt` is a whitelisted sort field on `file`.

- [ ] **Step 8: Run tests to verify they pass**

Run: `cd frontend && pnpm test buildNav MediaLibraryView`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add frontend/src/views/MediaLibraryView.vue frontend/src/views/MediaLibraryView.test.ts \
  frontend/src/router/index.ts frontend/src/lib/buildNav.ts frontend/src/lib/buildNav.test.ts \
  frontend/src/components/CollectionNav.vue
git commit -m "feat(frontend): Media Library view (grid + drag-drop upload + delete) + route/nav"
```

---

### Task 7: `FilePicker.vue`

**Files:**
- Create: `frontend/src/components/fields/FilePicker.vue`
- Test: `frontend/src/components/fields/FilePicker.test.ts`

**Interfaces:**
- Consumes: `itemsApi.list('file', …)`, `itemsApi.get('file', id)`, `MediaGrid` (Task 4), `FileRow` (Task 3), PrimeVue `Dialog`/`Button`/`InputText`.
- Produces: `FilePicker` (with `defineOptions({ name: 'FilePicker' })`) — props `{ modelValue: string | null; image?: boolean; disabled?: boolean }`; emits `(e: 'update:modelValue', v: string | null)`. Shows the current file (thumbnail if `image`, name otherwise), a **Select** button (opens a dialog with a searchable `MediaGrid`), and a **Clear** button. Selecting a tile emits the new id and closes the dialog. Deleted/inaccessible current id falls back to showing the raw id. Consumed by Task 8.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import FilePicker from './FilePicker.vue'
import { itemsApi } from '../../api/itemsApi'
import PrimeVue from 'primevue/config'

const mountOpts = { global: { plugins: [PrimeVue] } }
const rows = [{ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }]

describe('FilePicker', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('resolves and shows the current value on mount', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 } as never)
    const w = mount(FilePicker, { ...mountOpts, props: { modelValue: 'f1', image: true } })
    await flushPromises()
    expect(w.text()).toContain('a.png')
  })

  it('clear emits null', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 } as never)
    const w = mount(FilePicker, { ...mountOpts, props: { modelValue: 'f1' } })
    await flushPromises()
    ;(w.vm as unknown as { clear: () => void }).clear()
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([null])
  })

  it('selecting a file emits its id and closes the dialog', async () => {
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mount(FilePicker, { ...mountOpts, props: { modelValue: null } })
    await (w.vm as unknown as { openDialog: () => Promise<void> }).openDialog()
    await flushPromises()
    ;(w.vm as unknown as { onSelect: (id: string) => void }).onSelect('f1')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['f1'])
  })

  it('falls back to showing the id when the current file is gone', async () => {
    vi.spyOn(itemsApi, 'get').mockRejectedValue(new Error('404'))
    const w = mount(FilePicker, { ...mountOpts, props: { modelValue: 'ghost' } })
    await flushPromises()
    expect(w.text()).toContain('ghost')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test FilePicker`
Expected: FAIL — component does not exist.

- [ ] **Step 3: Implement**

```vue
<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import MediaGrid from '../media/MediaGrid.vue'
import FileThumbnail, { type FileRow } from '../media/FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'

defineOptions({ name: 'FilePicker' })

const props = defineProps<{ modelValue: string | null; image?: boolean; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string | null): void }>()

const langStore = useLanguageStore()
const current = ref<FileRow | null>(null)
const missingId = ref<string | null>(null)

const dialogOpen = ref(false)
const files = ref<FileRow[]>([])
const search = ref('')
const loadError = ref('')

async function resolveCurrent(): Promise<void> {
  current.value = null
  missingId.value = null
  if (!props.modelValue) return
  try {
    const row = await itemsApi.get('file', props.modelValue, { locale: langStore.defaultCode })
    current.value = row as unknown as FileRow
  } catch {
    missingId.value = props.modelValue // deleted / inaccessible -> show id
  }
}

async function loadOptions(): Promise<void> {
  loadError.value = ''
  try {
    const res = await itemsApi.list('file', {
      page: 0, rows: 50, search: search.value || undefined, locale: langStore.defaultCode || undefined,
    })
    files.value = res.data as unknown as FileRow[]
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : 'Failed to load files.'
  }
}

async function openDialog(): Promise<void> {
  dialogOpen.value = true
  await loadOptions()
}

function onSelect(id: string): void {
  emit('update:modelValue', id)
  dialogOpen.value = false
}

function clear(): void {
  emit('update:modelValue', null)
}

watch(() => props.modelValue, resolveCurrent)
watch(search, loadOptions)
onMounted(resolveCurrent)
defineExpose({ openDialog, onSelect, clear, resolveCurrent, loadOptions })
</script>

<template>
  <div class="file-picker">
    <div v-if="current" class="file-picker__current">
      <FileThumbnail v-if="image" :file="current" />
      <span>{{ current.fileName }}</span>
    </div>
    <span v-else-if="missingId" class="file-picker__missing">{{ missingId }}</span>
    <span v-else class="file-picker__empty">No file selected</span>

    <div class="file-picker__actions">
      <Button label="Select" size="small" :disabled="disabled" @click="openDialog" />
      <Button v-if="modelValue" label="Clear" size="small" text :disabled="disabled" @click="clear" />
    </div>

    <Dialog v-model:visible="dialogOpen" modal header="Select a file" :style="{ width: '60rem' }">
      <p v-if="loadError" class="error" role="alert">{{ loadError }}</p>
      <InputText v-model="search" placeholder="Search files…" class="file-picker__search" />
      <MediaGrid :files="files" selectable :selected-id="modelValue" @select="onSelect" />
    </Dialog>
  </div>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test FilePicker`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/FilePicker.vue frontend/src/components/fields/FilePicker.test.ts
git commit -m "feat(frontend): FilePicker (select-only File/Image field control + dialog)"
```

---

### Task 8: `FieldInput.vue` dispatch to `FilePicker`

**Files:**
- Modify: `frontend/src/components/fields/FieldInput.vue`
- Test: append to `frontend/src/components/fields/FieldInput.test.ts`

**Interfaces:**
- Consumes: `fieldInputKind` (Task 1), `FilePicker` (Task 7).
- Produces: for `kind === 'file'` and `kind === 'image'`, `FieldInput` renders `FilePicker` (with `:image="kind === 'image'"`) bound to the field value and relays `update:modelValue`.

- [ ] **Step 1: Write the failing test**

Append to `frontend/src/components/fields/FieldInput.test.ts`, reusing the file's existing mount harness / PrimeVue setup (bound below as `harness`):

```ts
it('renders FilePicker for image interface and relays the value', async () => {
  const w = mount(FieldInput, {
    ...harness, // reuse the file's existing mount options
    props: { field: { name: 'heroImageId', label: 'Hero', interface: 'image' }, modelValue: null },
  })
  const picker = w.findComponent({ name: 'FilePicker' })
  expect(picker.exists()).toBe(true)
  picker.vm.$emit('update:modelValue', 'f1')
  expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['f1'])
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm test FieldInput`
Expected: FAIL — no `file`/`image` branch; falls through to the readonly span.

- [ ] **Step 3: Implement**

In `frontend/src/components/fields/FieldInput.vue`, import the picker:

```ts
import FilePicker from './FilePicker.vue'
```

Add a branch **before** the final readonly `<span>` (mirror the existing `v-else-if` chain; `modelValue` is the field value, `isDisabled` is the file's existing disabled computed):

```vue
  <FilePicker
    v-else-if="kind === 'file' || kind === 'image'"
    :model-value="(modelValue as string | null)"
    :image="kind === 'image'"
    :disabled="isDisabled"
    @update:model-value="(v: string | null) => $emit('update:modelValue', v)"
  />
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm test FieldInput`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/FieldInput.vue frontend/src/components/fields/FieldInput.test.ts
git commit -m "feat(frontend): FieldInput dispatches file/image to FilePicker"
```

---

### Task 9: Sample `Article.HeroImageId` (backend) + regression green

**Files:**
- Modify: the sample `Article` entity (locate via grep).
- Test: existing backend suite (regression guard only).

**Interfaces:**
- Produces: sample `article` collection gains a `heroImageId` field with `Interface = FieldInterface.Image`, exercised by the live gate.

- [ ] **Step 1: Locate the sample Article entity**

Run: `grep -rln "class Article" samples/`
Expected: the `Article` entity file path.

- [ ] **Step 2: Add the field**

Add to `Article` (mirror `SeoTranslation.SeoOgImageId` — a nullable `Guid` scalar with the Image interface):

```csharp
[CmsField(Label = "Hero Image", Interface = FieldInterface.Image, Group = "Content", Sort = 20)]
public Guid? HeroImageId { get; set; }
```

> Confirm the actual `Group`/`Sort` values by reading the neighbouring Article fields rather than assuming `"Content"`/`20`; use values consistent with the file.

- [ ] **Step 3: Run backend suite (regression)**

Run: `dotnet test`
Expected: PASS — all tests green (metadata scan picks up the new field; no behaviour change).

- [ ] **Step 4: Commit**

```bash
git add samples/Struo.Sample.Blog
git commit -m "test(sample): add Article.HeroImageId (Image) for Phase 7e media picker live gate"
```

---

### Task 10: Full verification gate + docs

**Files:**
- Modify: `docs/ROADMAP.md` (flip Phase 7e row to done; add spec/plan links; refresh the verification-baseline note).

- [ ] **Step 1: Backend gate**

Run: `dotnet build && dotnet test`
Expected: build clean (warnings-as-errors); all tests pass.

- [ ] **Step 2: Frontend gate**

Run: `cd frontend && pnpm test && pnpm build`
Expected: all unit/component tests pass; build succeeds.

- [ ] **Step 3: Live gate (real Postgres + Redis + MinIO/S3)**

Follow `frontend/e2e/README.md` to run the dev API against live infra, then:
- Open **Media Library** → drag-drop **multiple** files → confirm each uploads (201) and appears in the grid; confirm a rejected upload (oversized/bad type) surfaces an inline error without blocking siblings.
- Edit a file's **Title/Alt** (per-locale) via the edit action → `PUT /api/items/file/{id}` → re-open confirms persistence.
- Open an **Article** → **Select** the hero image via the picker → save → `POST/PUT /api/items/article` persists `heroImageId`.
- `GET /api/items/article/{id}?locale=en` returns the article with `heroImageId` set; the picker shows the thumbnail on re-open.
- **Clear** the hero image → save → `heroImageId` is null on re-read.
- **Delete** a file in the library (`DELETE /api/files/{id}` → 204); a picker referencing the deleted id falls back to showing the id (no crash).
- Record evidence (commands + responses) as in prior phases' live-gate notes.

- [ ] **Step 4: Update ROADMAP + commit**

Flip the Phase 7e row to done with the live-verified note; link the spec/plan; refresh the verification-baseline counts.

```bash
git add docs/ROADMAP.md
git commit -m "docs: Phase 7e done + live-verified (media library + File/Image pickers)"
```

---

## Self-Review

**Spec coverage:**
- Media Library view (grid + drag-drop bulk upload + manage) — Tasks 4, 5, 6. ✓
- Reuse `GET /api/items/file` for listing — Task 6. ✓
- Thumbnail via `/api/files/{id}/content` — Tasks 2 (contentUrl), 3. ✓
- Drag-drop parallel upload with isolated per-file errors — Task 5. ✓
- Edit per-locale Title/Alt via existing item form — Task 6 (`onEdit` → `collection-item`). ✓
- Delete file — Task 6 (`onDelete` → `filesApi.remove`). ✓
- Select-only File/Image field picker storing `Guid?` — Tasks 7, 8. ✓
- Deleted-file fallback to id — Task 7 (`missingId`). ✓
- Dedicated `/media` route + nav entry, `file` suppressed from auto nav — Task 6. ✓
- `fieldInputKind` file/image — Task 1. ✓
- No save-path change (value is a scalar `Guid?`) — Task 8 relays the value; `buildItemPayload` unchanged. ✓
- Sample `Article.HeroImageId` — Task 9. ✓
- Verification incl. user-driven live gate — Task 10. ✓
- Error handling (§5): upload per-file inline error (Task 5), picker load/permission error (Task 7 `loadError`), thumbnail load failure → chip (Task 3), deleted reference → id (Task 7), delete error surfaced (Task 6). ✓
- Out of scope respected: no `Files` multi, no inline upload in picker, no transforms/folders. ✓

**Placeholder scan:** No TBD/TODO in code steps; each code step shows full content. Task 8's test references "reuse the file's existing mount harness" for *setup* only (those PrimeVue mount options already exist in `FieldInput.test.ts`) with the concrete assertion given — reproducing the harness verbatim would be guesswork about the current file. Task 9 Step 2 flags confirming the existing `Group`/`Sort` convention by reading the file rather than inventing values.

**Type consistency:** `FileRow = { id; fileName; contentType; size }` defined+exported in Task 3, imported by Tasks 4/6/7. `FileMeta` (Task 2) is the upload return, consumed by Task 5's `uploaded` event. `filesApi.upload/remove/contentUrl` signatures (Task 2) used identically in Tasks 3/5/6. `FilePicker` props `{ modelValue: string | null; image?; disabled? }` and its `update:modelValue` (Task 7) match the dispatch in Task 8. `fieldInputKind` returns `'file' | 'image'` (Task 1) consumed by Task 8's `v-else-if`. `itemsApi.list/get` signatures match the existing api. ✓
