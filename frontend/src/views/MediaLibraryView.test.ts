import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import MediaLibraryView from './MediaLibraryView.vue'
import MediaUploadDialog from '../components/media/MediaUploadDialog.vue'
import { itemsApi } from '../api/itemsApi'
import { ApiError } from '../api/apiClient'
import { useAuthStore } from '../stores/authStore'
import type { CurrentUser } from '../stores/authStore'
import type { FolderRow } from '../lib/folderTree'

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

const { confirmRequire, toastAdd } = vi.hoisted(() => ({ confirmRequire: vi.fn(), toastAdd: vi.fn() }))
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    title: 'Media Library', count: '{n} files', upload: 'Upload', searchPlaceholder: 'Search files…',
    typeAll: 'All types', typeImage: 'Images', typeVideo: 'Video', sortNewest: 'Newest', sortName: 'By name',
    viewGrid: 'Grid view', viewList: 'List view', empty: 'No media files', loadFailed: 'Failed to load media',
    folderNew: 'New folder', folderName: 'Folder name', folderConfirm: 'OK',
    folderRename: 'Rename folder', folderDelete: 'Delete folder',
    folderDeleteConfirm: 'Delete folder "{name}"?', folderNotEmpty: 'Folder is not empty',
    folderLoadFailed: 'Failed to load folders', folderSaveFailed: 'Folder operation failed',
    breadcrumbRoot: 'Media Library',
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
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>', props: ['label', 'disabled'] },
  ConfirmDialog: { name: 'ConfirmDialog', template: '<div />', props: ['group'] },
  MediaDetailDialog: { name: 'MediaDetailDialog', template: '<div />', props: ['file', 'canWrite', 'canDelete'] },
}

function seedUser(perms: Partial<Record<'read' | 'write' | 'delete', boolean>>, collection = 'file'): void {
  const auth = useAuthStore()
  auth.user = { id: 'u1', isSuperAdmin: false, permissions: { [collection]: { read: true, write: false, delete: false, ...perms } } } as CurrentUser
}

function mountView() {
  return mount(MediaLibraryView, { global: { plugins: [i18n], stubs } })
}

/** Routes itemsApi.list by collection: 'mediafolder' always resolves to `folders`; 'file' calls
 *  pop sequentially from `fileResults` (repeating the last entry once exhausted), matching the
 *  hand-authored `.mockResolvedValueOnce` chains the FE-27 tests relied on before folders existed. */
function makeListMock(fileResults: Array<{ data: unknown; total: number }>, folders: FolderRow[] = []) {
  let i = 0
  return vi.spyOn(itemsApi, 'list').mockImplementation((collection: string) => {
    if (collection === 'mediafolder') return Promise.resolve({ data: folders as never, total: folders.length })
    const result = fileResults[Math.min(i, fileResults.length - 1)]
    i += 1
    return Promise.resolve(result as never)
  })
}

describe('MediaLibraryView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    confirmRequire.mockReset()
    toastAdd.mockReset()
  })

  it('loads files into the grid on mount', async () => {
    makeListMock([{ data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('file', expect.objectContaining({ page: 0 }))
    expect(w.findAll('.media-tile')).toHaveLength(1)
  })

  it('applies the image type filter and reloads', async () => {
    const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onType: (t: string) => void }).onType('image')
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({
      filter: { contentType: { op: '_starts_with', value: 'image/' }, folderId: { op: '_null', value: 'true' } },
    }))
  })

  it('sorts by name', async () => {
    const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onSort: (s: string) => void }).onSort('name')
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ sort: 'fileName' }))
  })

  it('opens the detail dialog for a clicked tile', async () => {
    makeListMock([{ data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    await w.find('.media-tile').trigger('click')
    expect(w.findComponent({ name: 'MediaDetailDialog' }).props('file')).toEqual(rows[0])
  })

  it('reloads once per upload batch (dialog done event)', async () => {
    const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    const fileCalls = () => list.mock.calls.filter((c) => c[0] === 'file').length
    expect(fileCalls()).toBe(1)
    w.findComponent(MediaUploadDialog).vm.$emit('done')
    await flushPromises()
    expect(fileCalls()).toBe(2)
  })

  it('stays on the current page after a delete when it is still in range (FE-27)', async () => {
    const list = makeListMock([
      { data: rows, total: 100 }, // mount (page 0)
      { data: rows, total: 100 }, // onPage(1)
      { data: rows, total: 100 }, // onDeleted's refresh at page 1
    ])
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onPage: (e: { page: number; rows: number }) => void }).onPage({ page: 1, rows: 24 })
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ page: 1 }))
    await (w.vm as unknown as { onDeleted: () => void }).onDeleted()
    await flushPromises()
    // total (100) still covers page 1 (24 rows/page -> 5 pages, indices 0-4), so the delete
    // refresh must not reset the user back to page 0.
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ page: 1 }))
    // FE-27: exactly one reload for the delete -- mount (1) + onPage (2) + onDeleted's single
    // refresh (3) file-scoped calls. (A separate 'mediafolder' call also fires at mount.)
    expect(list.mock.calls.filter((c) => c[0] === 'file')).toHaveLength(3)
  })

  it('clamps to the last valid page after a delete strands the current page out of range (FE-27)', async () => {
    const list = makeListMock([
      { data: rows, total: 1 },  // mount (page 0)
      { data: rows, total: 73 }, // onPage(2) -- page 2 valid (3 pages)
      { data: [], total: 30 },   // onDeleted's refresh at page 2 -- now out of range (2 pages left)
      { data: rows, total: 30 }, // clamped reload at page 1 (last valid page)
    ])
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onPage: (e: { page: number; rows: number }) => void }).onPage({ page: 2, rows: 24 })
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ page: 2 }))
    await (w.vm as unknown as { onDeleted: () => void }).onDeleted()
    await flushPromises()
    const fileCalls = list.mock.calls.filter((c) => c[0] === 'file')
    // Deleting the last item on the last page must clamp to the new last valid page (1),
    // never reset all the way back to page 0 and never leave the user on the stale, now
    // out-of-range page 2.
    expect(fileCalls[2]).toEqual(['file', expect.objectContaining({ page: 2 })])
    expect(fileCalls[3]).toEqual(['file', expect.objectContaining({ page: 1 })])
    expect(fileCalls).toHaveLength(4)
  })

  it('hides Upload for a user without write on file', async () => {
    makeListMock([{ data: rows, total: 1 }])
    const w = mountView()
    seedUser({ write: false })
    await flushPromises()
    const labels = w.findAllComponents({ name: 'Button' }).map((b) => b.props('label'))
    expect(labels).not.toContain('Upload')
  })

  it('shows root-level folders on initial load and scopes the file filter to the root', async () => {
    const folders: FolderRow[] = [
      { id: 'a', name: 'A', parentId: null },
      { id: 'b', name: 'B', parentId: 'a' },
    ]
    const list = makeListMock([{ data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    const cards = w.findAll('.folder-card')
    expect(cards).toHaveLength(1)
    expect(cards[0].text()).toContain('A')
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({
      filter: expect.objectContaining({ folderId: { op: '_null', value: 'true' } }),
    }))
  })

  it('enters a folder: scopes the file filter, shows the breadcrumb, and lists child folders', async () => {
    const folders: FolderRow[] = [
      { id: 'a', name: 'A', parentId: null },
      { id: 'b', name: 'B', parentId: 'a' },
    ]
    const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { enterFolder: (id: string) => void }).enterFolder('a')
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({
      filter: expect.objectContaining({ folderId: { op: '_eq', value: 'a' } }),
    }))
    expect(w.find('.media-crumb').text()).toContain('A')
    const cards = w.findAll('.folder-card')
    expect(cards).toHaveLength(1)
    expect(cards[0].text()).toContain('B')
  })

  it('hides folder cards and drops the folder filter during a global search', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    expect(w.findAll('.folder-card')).toHaveLength(1)
    await (w.vm as unknown as { onSearchInput: (v: string) => void }).onSearchInput('logo')
    await new Promise((resolve) => setTimeout(resolve, 320))
    await flushPromises()
    expect(w.findAll('.folder-card')).toHaveLength(0)
    const lastFileCall = list.mock.calls.filter((c) => c[0] === 'file').at(-1)
    expect(lastFileCall?.[1]).not.toHaveProperty('filter.folderId')
    expect((lastFileCall?.[1] as { filter?: Record<string, unknown> }).filter ?? {}).not.toHaveProperty('folderId')
  })

  it('creates a folder scoped to the current folder', async () => {
    makeListMock([{ data: rows, total: 1 }], [])
    const create = vi.spyOn(itemsApi, 'create').mockResolvedValue({})
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onCreateFolder: (n: string) => Promise<void> }).onCreateFolder('N')
    await flushPromises()
    expect(create).toHaveBeenCalledWith('mediafolder', { name: 'N', parentId: null })
  })

  it('shows a folderNotEmpty toast when deleting a non-empty folder (409 CONFLICT)', async () => {
    const target: FolderRow = { id: 'a', name: 'A', parentId: null }
    makeListMock([{ data: rows, total: 1 }], [target])
    vi.spyOn(itemsApi, 'remove').mockRejectedValue(new ApiError(409, 'conflict', 'CONFLICT'))
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { onRemoveFolder: (f: FolderRow) => void }).onRemoveFolder(target)
    expect(confirmRequire).toHaveBeenCalledWith(expect.objectContaining({ group: 'media-folder' }))
    const accept = confirmRequire.mock.calls[0][0].accept as () => Promise<void>
    await accept()
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'warn', summary: 'Folder is not empty' }))
  })

  it('combines the type filter and folder filter when both apply', async () => {
    const list = makeListMock(
      [{ data: rows, total: 1 }, { data: rows, total: 1 }, { data: rows, total: 1 }],
      [{ id: 'a', name: 'A', parentId: null }],
    )
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { enterFolder: (id: string) => void }).enterFolder('a')
    await flushPromises()
    await (w.vm as unknown as { onType: (t: string) => void }).onType('image')
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({
      filter: { contentType: { op: '_starts_with', value: 'image/' }, folderId: { op: '_eq', value: 'a' } },
    }))
  })

  it('passes the current folder id to the upload dialog', async () => {
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], [{ id: 'a', name: 'A', parentId: null }])
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { enterFolder: (id: string) => void }).enterFolder('a')
    await flushPromises()
    expect(w.findComponent(MediaUploadDialog).props('folderId')).toBe('a')
  })
})
