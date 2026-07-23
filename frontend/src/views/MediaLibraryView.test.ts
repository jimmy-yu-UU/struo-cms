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

  it('stays on the current page after a delete when it is still in range (FE-27)', async () => {
    const list = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 100 })
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
    // refresh (3). A redundant extra load at the same page would still pass the LastCalledWith
    // assertion above, so pin the call count too.
    expect(list).toHaveBeenCalledTimes(3)
  })

  it('clamps to the last valid page after a delete strands the current page out of range (FE-27)', async () => {
    const list = vi.spyOn(itemsApi, 'list')
      .mockResolvedValueOnce({ data: rows as never, total: 1 }) // initial mount load (page 0)
      .mockResolvedValueOnce({ data: rows as never, total: 73 }) // onPage(2) load -- page 2 valid (3 pages)
      .mockResolvedValueOnce({ data: [] as never, total: 30 }) // onDeleted's refresh at page 2 -- now out of range (2 pages left)
      .mockResolvedValueOnce({ data: rows as never, total: 30 }) // clamped reload at page 1 (last valid page)
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onPage: (e: { page: number; rows: number }) => void }).onPage({ page: 2, rows: 24 })
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ page: 2 }))
    await (w.vm as unknown as { onDeleted: () => void }).onDeleted()
    await flushPromises()
    // Deleting the last item on the last page must clamp to the new last valid page (1),
    // never reset all the way back to page 0 and never leave the user on the stale, now
    // out-of-range page 2.
    expect(list).toHaveBeenNthCalledWith(3, 'file', expect.objectContaining({ page: 2 }))
    expect(list).toHaveBeenNthCalledWith(4, 'file', expect.objectContaining({ page: 1 }))
    expect(list).toHaveBeenCalledTimes(4)
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
