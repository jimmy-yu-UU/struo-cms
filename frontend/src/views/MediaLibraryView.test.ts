import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import MediaLibraryView from './MediaLibraryView.vue'
import MediaUploadDialog from '../components/media/MediaUploadDialog.vue'
import MediaGrid from '../components/media/MediaGrid.vue'
import MediaFolderCards from '../components/media/MediaFolderCards.vue'
import MediaFileList from '../components/media/MediaFileList.vue'
import MediaMoveDialog from '../components/media/MediaMoveDialog.vue'
import MediaSelectionToolbar from '../components/media/MediaSelectionToolbar.vue'
import { itemsApi } from '../api/itemsApi'
import { filesApi } from '../api/filesApi'
import { ApiError } from '../api/apiClient'
import { useAuthStore } from '../stores/authStore'
import type { CurrentUser } from '../stores/authStore'
import type { FolderRow } from '../lib/folderTree'
import { DRAG_MIME, serializeMovePayload, type MovePayload } from '../lib/mediaMove'

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

const { confirmRequire, toastAdd } = vi.hoisted(() => ({ confirmRequire: vi.fn(), toastAdd: vi.fn() }))
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    title: 'Media Library', count: '{n} files', upload: 'Upload', searchPlaceholder: 'Search files…',
    typeAll: 'All types', typeImage: 'Images', typeVideo: 'Video', sortNewest: 'Newest', sortName: 'By name',
    typeFilter: 'File type', sortFilter: 'Sort order',
    viewGrid: 'Grid view', viewList: 'List view', empty: 'No media files', loadFailed: 'Failed to load media',
    folderNew: 'New folder', folderName: 'Folder name', folderConfirm: 'OK',
    folderRename: 'Rename folder', folderDelete: 'Delete folder',
    folderDeleteConfirm: 'Delete folder "{name}"?', folderNotEmpty: 'Folder is not empty',
    folderLoadFailed: 'Failed to load folders', folderSaveFailed: 'Folder operation failed',
    breadcrumbRoot: 'Media Library',
    moveFailed: 'Move failed', moveSkippedCycle: '{n} folder(s) skipped: a folder cannot be moved into itself',
    moved: 'Moved {n} item(s)', moveTo: 'Move to…', moveRoot: 'Root', moveSubmit: 'Move',
    menuOpen: 'Open', menuMove: 'Move to…', menuRename: 'Rename', menuDelete: 'Delete',
    menuNewFolder: 'New folder', menuUpload: 'Upload',
    selectionCount: '{n} selected', selectionClear: 'Clear selection',
  }, collectionList: {
    range: 'Showing {from}–{to} of {total}', active: 'Active', trash: 'Trash',
    restore: 'Restore', purge: 'Delete permanently', trashNotice: 'You are viewing the trash.',
  } } },
})

const rows = [{ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1, width: 10, height: 10, createdAt: '2026-07-01T00:00:00Z' }]

const stubs = {
  PageHeader: { name: 'PageHeader', template: '<div><slot name="actions" /></div>' },
  ListToolbar: { name: 'ListToolbar', template: '<div><slot name="filters" /></div>', props: ['searchValue', 'searchPlaceholder'] },
  MediaDetailDialog: { name: 'MediaDetailDialog', template: '<div />', props: ['file', 'canWrite', 'canDelete'] },
  // reka's own portal wrapper is itself named Teleport and collides with VTU's stub, dropping the
  // slot content of every floating control on the page (the two Selects' option lists).
  teleport: true,
}

function seedUser(perms: Partial<Record<'read' | 'write' | 'delete', boolean>>, collection = 'file'): void {
  const auth = useAuthStore()
  auth.user = { id: 'u1', isSuperAdmin: false, permissions: { [collection]: { read: true, write: false, delete: false, ...perms } } } as CurrentUser
}

function mountView() {
  return mount(MediaLibraryView, { global: { plugins: [i18n], stubs, renderStubDefaultSlot: true } })
}

/** Routes itemsApi.list by collection: 'mediafolder' always resolves to `folders`; 'file' calls
 *  pop sequentially from `fileResults` (repeating the last entry once exhausted), matching the
 *  hand-authored `.mockResolvedValueOnce` chains the tests below relied on before folders existed. */
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
    confirmRequire.mockReset(); confirmRequire.mockResolvedValue(true)
    toastAdd.mockReset()
    vi.spyOn(itemsApi, 'update').mockResolvedValue({})
  })

  it('renders the vendored controls', async () => {
    makeListMock([{ data: rows, total: 1 }])
    seedUser({ write: true, delete: true })
    const w = mountView()
    await flushPromises()
    // Three Selects (type, sort, and DataTablePagination's own page-size control -- total:1 still
    // renders the pager since the v-if is `total > 0`, not `total > perPage`, see the "renders the
    // pager for a single page of results" test below) and two ToggleGroups (Active/Trash,
    // grid/list).
    expect(w.findAll('[data-slot="select-trigger"]')).toHaveLength(3)
    expect(w.findAll('[data-slot="toggle-group"]')).toHaveLength(2)
  })

  it('shows the current type filter on the Select trigger, and follows it after mount', async () => {
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    // Inbound direction: the trigger must render the option matching the stored value, not a
    // placeholder. `type` starts at 'all', whose label is 'All types'.
    expect(w.findAll('[data-slot="select-trigger"]')[0].text()).toContain('All types')
    // The initial-render assertion above cannot distinguish a controlled Select from a
    // `default-value` one (both render the same on the first paint) -- also change the value
    // AFTER mount, through the same path a real re-render would take, and confirm the trigger
    // followed it.
    await (w.vm as unknown as { onType: (t: string) => void }).onType('image')
    await flushPromises()
    expect(w.findAll('[data-slot="select-trigger"]')[0].text()).toContain('Images')
  })

  it('shows the current sort filter on the Select trigger, and follows it after mount', async () => {
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    // Inbound direction: `sort` starts at 'newest', whose label is 'Newest'.
    expect(w.findAll('[data-slot="select-trigger"]')[1].text()).toContain('Newest')
    // Change the value after mount through the same path a real re-render would take (the
    // view's own onSort handler) and confirm the trigger followed it.
    await (w.vm as unknown as { onSort: (s: string) => void }).onSort('name')
    await flushPromises()
    expect(w.findAll('[data-slot="select-trigger"]')[1].text()).toContain('By name')
  })

  it('reloads with the image filter when the type Select is opened and Images is picked', async () => {
    const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    // jsdom CAN dispatch reka's pointerdown/pointerup (see vitest.setup.ts and
    // UiLanguageSwitcher.test.ts) -- drive the real interaction instead of emitting from the
    // vendored child, which never renders SelectContent/SelectItem at all and so cannot catch a
    // wrong :value, a missing SelectContent, or a broken option list. Two Select comboboxes share
    // this page (type, sort), so scope the option lookup to this trigger's own aria-controls
    // target instead of searching the whole page for a matching option text, which would also
    // match the sort Select's options if a label ever collided across the two lists.
    const trigger = w.findAll('[data-slot="select-trigger"]')[0]
    await trigger.trigger('pointerdown')
    await flushPromises()
    const content = w.find(`#${trigger.attributes('aria-controls')}`)
    const option = content.findAll('[role="option"]').find((o) => o.text() === 'Images')
    expect(option).toBeDefined()
    await option!.trigger('pointerup')
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({
      filter: { contentType: { op: '_starts_with', value: 'image/' }, folderId: { op: '_null', value: 'true' } },
    }))
  })

  it('ignores the ToggleGroup deselect emit so the trash switch has no "neither" state', async () => {
    makeListMock([{ data: rows, total: 1 }])
    seedUser({ delete: true })
    const w = mountView()
    await flushPromises()
    const vm = w.vm as unknown as { mode: string; onModeToggle: (v: unknown) => void }
    vm.onModeToggle('trash')
    await flushPromises()
    expect(vm.mode).toBe('trash')
    // reka's single-type ToggleGroup emits undefined when the pressed item is clicked again.
    vm.onModeToggle(undefined)
    await flushPromises()
    expect(vm.mode).toBe('trash')
  })

  it('reflects the trash-switch ToggleGroup\'s real DOM state, and follows a mode change after mount', async () => {
    const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    seedUser({ delete: true })
    const w = mountView()
    await flushPromises()
    const activeItem = () => w.find('[data-slot="toggle-group-item"][value="active"]')
    const trashItem = () => w.find('[data-slot="toggle-group-item"][value="trash"]')
    // Inbound direction: `mode` starts at 'active', so the Active item must report pressed and
    // the Trash item must not -- through reka's own state attributes, not a Tailwind class.
    expect(activeItem().attributes('data-state')).toBe('on')
    expect(activeItem().attributes('aria-pressed')).toBe('true')
    expect(trashItem().attributes('data-state')).toBe('off')

    // A real click on this exact item cannot distinguish a controlled ToggleGroup from an
    // uncontrolled one: reka updates its own pressed-state locally on click regardless of
    // whether `model-value` or `default-value` drives it. Go through `setMode` instead --
    // the same production path `onModeToggle` runs when the emit fires -- to change `mode`
    // through the actual reactive source and confirm the DOM (not just the app-level `mode`
    // ref) followed it. If the trash switch silently stopped tracking `mode`, a user looking
    // at the trash would still see "Active" lit.
    ;(w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
    await flushPromises()
    expect(trashItem().attributes('data-state')).toBe('on')
    expect(activeItem().attributes('data-state')).toBe('off')
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ deleted: 'only' }))

    // And the reverse really is wired the other way: a real click on the DOM drives `mode`
    // back out through the toggle's own emit.
    await activeItem().trigger('click')
    await flushPromises()
    expect((w.vm as unknown as { mode: string }).mode).toBe('active')
  })

  it('reflects the view ToggleGroup\'s real DOM state, and follows a view change after mount', async () => {
    makeListMock([{ data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    const gridItem = () => w.find('[data-slot="toggle-group-item"][value="grid"]')
    const listItem = () => w.find('[data-slot="toggle-group-item"][value="list"]')
    // Inbound direction: `view` starts at 'grid'.
    expect(gridItem().attributes('data-state')).toBe('on')
    expect(listItem().attributes('data-state')).toBe('off')
    expect(w.findComponent({ name: 'MediaFileList' }).exists()).toBe(false)

    // As above: a real click on this item can't tell controlled from uncontrolled, since reka
    // updates its own pressed-state on click either way. Go through `onViewToggle` -- the exact
    // handler the toggle's own emit runs -- to change `view` through the real reactive source
    // and confirm the DOM followed.
    ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
    await flushPromises()
    expect(listItem().attributes('data-state')).toBe('on')
    expect(gridItem().attributes('data-state')).toBe('off')
    expect(w.findComponent({ name: 'MediaFileList' }).exists()).toBe(true)

    // And a real click really does drive `view` back out through onViewToggle.
    await gridItem().trigger('click')
    await flushPromises()
    expect((w.vm as unknown as { view: string }).view).toBe('grid')
  })

  it('renders a distinct lucide icon in every icon-bearing control, never reusing one for two meanings', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    // seedUser only ever sets ONE collection's permissions (it replaces auth.user wholesale) --
    // both the New-folder button (mediafolder write) and the Upload button (file write) must be
    // visible at once here, so set both collections directly in one assignment.
    const auth = useAuthStore()
    auth.user = {
      id: 'u1', isSuperAdmin: false,
      permissions: {
        file: { read: true, write: true, delete: true },
        mediafolder: { read: true, write: true, delete: false },
      },
    } as CurrentUser
    await flushPromises()

    // PageHeader actions: New folder (FolderPlus) and Upload (Upload) are two different icons.
    const newFolderButton = w.findAllComponents({ name: 'Button' }).find((b) => b.text().includes('New folder'))
    const uploadButton = w.findAllComponents({ name: 'Button' }).find((b) => b.text().includes('Upload'))
    expect(newFolderButton?.find('.lucide-folder-plus').exists()).toBe(true)
    expect(uploadButton?.find('.lucide-upload').exists()).toBe(true)

    // View-toggle icons must be two DIFFERENT icons -- it's an icon-only control, so if both were
    // the same component the two buttons would be visually indistinguishable.
    expect(w.find('[data-slot="toggle-group-item"][value="grid"] .lucide-layout-grid').exists()).toBe(true)
    expect(w.find('[data-slot="toggle-group-item"][value="grid"] .lucide-list').exists()).toBe(false)
    expect(w.find('[data-slot="toggle-group-item"][value="list"] .lucide-list').exists()).toBe(true)
    expect(w.find('[data-slot="toggle-group-item"][value="list"] .lucide-layout-grid').exists()).toBe(false)

    // Breadcrumb separator (ChevronRight), reachable once a folder is entered.
    await (w.vm as unknown as { enterFolder: (id: string) => void }).enterFolder('a')
    await flushPromises()
    expect(w.find('.media-crumb .lucide-chevron-right').exists()).toBe(true)

    // Trash banner icon (Trash2), and the two trash-row action icons -- restore (Undo2) must be
    // different from purge (Trash2), not both Trash2.
    ;(w.vm as unknown as { setMode: (m: string) => void }).setMode('trash')
    await flushPromises()
    expect(w.find('.trash-banner .lucide-trash-2').exists()).toBe(true)
    const rowButtons = w.findAll('.media-tile__actions button')
    expect(rowButtons[0].find('.lucide-undo-2').exists()).toBe(true)
    expect(rowButtons[0].find('.lucide-trash-2').exists()).toBe(false)
    expect(rowButtons[1].find('.lucide-trash-2').exists()).toBe(true)
  })

  it('gives the Selects, view toggle, and trash-row actions real accessible names', async () => {
    seedUser({ delete: true })
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    // reka's role="combobox" trigger has no field-identifying accessible name of its own — its
    // rendered SelectValue announces the current option, not which field it belongs to;
    // media.typeFilter/media.sortFilter exist specifically to give it one.
    const triggers = w.findAll('[data-slot="select-trigger"]')
    expect(triggers[0].attributes('aria-label')).toBe('File type')
    expect(triggers[1].attributes('aria-label')).toBe('Sort order')
    expect(w.find('[data-slot="toggle-group-item"][value="grid"]').attributes('aria-label')).toBe('Grid view')
    expect(w.find('[data-slot="toggle-group-item"][value="list"]').attributes('aria-label')).toBe('List view')

    ;(w.vm as unknown as { setMode: (m: string) => void }).setMode('trash')
    await flushPromises()
    const rowButtons = w.findAll('.media-tile__actions button')
    expect(rowButtons[0].attributes('aria-label')).toBe('Restore')
    expect(rowButtons[1].attributes('aria-label')).toBe('Delete permanently')
  })

  it('gives every trash-list row action an explicit type="button"', async () => {
    makeListMock([{ data: rows, total: 1 }])
    seedUser({ delete: true })
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { setMode: (m: string) => void }).setMode('trash')
    await flushPromises()
    const actions = w.findAll('.media-tile__actions button')
    expect(actions.length).toBeGreaterThan(0)
    actions.forEach((b) => expect(b.attributes('type')).toBe('button'))
  })

  // FileThumbnail renders for real in this view (it is not among the stubs above) -- asserting
  // the rendered `data-size` attribute rather than a prop read fails if the list's size="sm"
  // binding is ever dropped. The trash is now rendered through the same MediaFileList as the
  // active list, so the sm-sizing behaviour is exercised through the list view.
  it('gives the trash-list thumbnail the sm size', async () => {
    makeListMock([{ data: rows, total: 1 }])
    seedUser({ delete: true })
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
    ;(w.vm as unknown as { setMode: (m: string) => void }).setMode('trash')
    await flushPromises()
    const thumbs = w.findAll('.media-list__thumb .file-thumb')
    expect(thumbs.length).toBeGreaterThan(0)
    for (const thumb of thumbs) expect(thumb.attributes('data-size')).toBe('sm')
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

  // A soft-deleted file must stay unopenable so a user can't press Save/Delete on it in
  // MediaDetailDialog. Trash mode renders through the same MediaGrid whose whole tile IS
  // clickable in active mode, so that protection has to be asserted explicitly instead of
  // relying on there being no click handler.
  it('does not open the detail dialog when a tile is activated in trash mode', async () => {
    seedUser({ delete: true })
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
    await flushPromises()
    await w.find('.media-tile').trigger('click')
    expect(w.findComponent({ name: 'MediaDetailDialog' }).props('file')).toBeNull()
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

  it('renders DataTablePagination instead of a Paginator plus TableFooter', async () => {
    makeListMock([{ data: rows, total: 100 }])
    const w = mountView()
    await flushPromises()
    expect(w.findComponent({ name: 'DataTablePagination' }).exists()).toBe(true)
    expect(w.findComponent({ name: 'TableFooter' }).exists()).toBe(false)
    expect(w.findComponent({ name: 'Paginator' }).exists()).toBe(false)
  })

  it('passes the media page size through and reloads on a page change', async () => {
    // The second load returns a different total (55, not 100) specifically so the total-prop
    // assertion below can distinguish a real binding from a hardcoded `:total="100"` -- both
    // would look identical on the very first render.
    const list = makeListMock([{ data: rows, total: 100 }, { data: rows, total: 55 }])
    const w = mountView()
    await flushPromises()
    const pager = w.findComponent({ name: 'DataTablePagination' })
    // 24 is the media library's own page size and is not in DataTablePagination's default
    // pageSizeOptions ([10, 25, 50, 100]) -- reka's Select shows a blank trigger label when its
    // model-value matches no SelectItem (see DataTablePagination.vue's pageSizeOptions JSDoc for
    // how that was confirmed), so the override must be supplied.
    expect(pager.props('pageSize')).toBe(24)
    expect(pager.props('pageSizeOptions')).toContain(24)
    await pager.vm.$emit('update:page', 2)
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ page: 2, rows: 24 }))
    // Inbound direction: the emit above only proves onPageChange's own argument reached the API
    // call, which stays true even if the template's `:page` binding were hardcoded to a literal --
    // it never reads the prop back from the pager. Assert the pager's own `page` prop followed the
    // resulting state change too, closing that gap.
    expect(pager.props('page')).toBe(2)
    // Same gap for `:total` -- it changes across loads in production (the clamping tests below
    // drive it 73 -> 30) and feeds both the range text and the next-button disabled state, so a
    // hardcoded literal would silently freeze both.
    expect(pager.props('total')).toBe(55)
  })

  it('returns to the first page when the page size changes', async () => {
    const list = makeListMock([{ data: rows, total: 100 }, { data: rows, total: 100 }, { data: rows, total: 100 }])
    const w = mountView()
    await flushPromises()
    const pager = w.findComponent({ name: 'DataTablePagination' })
    await pager.vm.$emit('update:page', 2)
    await flushPromises()
    await pager.vm.$emit('update:pageSize', 48)
    await flushPromises()
    // An offset computed against the old page size is meaningless against the new one.
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ page: 0, rows: 48 }))
    // Inbound direction for `:page-size`: the API-call assertion above only proves
    // onPageSizeChange's own argument reached the load call, which stays true even if the
    // template's `:page-size` binding were hardcoded to a literal -- it never reads the prop back
    // from the pager. The rows-per-page <select> would otherwise keep showing 24 while the API is
    // already serving 48.
    expect(pager.props('pageSize')).toBe(48)
  })

  it('renders the pager for a single page of results', async () => {
    // The pager shows whenever total > 0 because DataTablePagination also carries the range text
    // and rows-per-page selector, both useful even when everything fits on one page. total (5) is
    // well under the media library's pageSize (24), pinning that a single partial page still renders it.
    makeListMock([{ data: rows, total: 5 }])
    const w = mountView()
    await flushPromises()
    expect(w.findComponent({ name: 'DataTablePagination' }).exists()).toBe(true)
  })

  it('hides the pager when there are no results', async () => {
    makeListMock([{ data: [], total: 0 }])
    const w = mountView()
    await flushPromises()
    expect(w.findComponent({ name: 'DataTablePagination' }).exists()).toBe(false)
  })

  it('stays on the current page after a delete when it is still in range', async () => {
    const list = makeListMock([
      { data: rows, total: 100 }, // mount (page 0)
      { data: rows, total: 100 }, // onPageChange(1)
      { data: rows, total: 100 }, // onDeleted's refresh at page 1
    ])
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onPageChange: (page: number) => void }).onPageChange(1)
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ page: 1 }))
    await (w.vm as unknown as { onDeleted: () => void }).onDeleted()
    await flushPromises()
    // total (100) still covers page 1 (24 rows/page -> 5 pages, indices 0-4), so the delete
    // refresh must not reset the user back to page 0.
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ page: 1 }))
    // Exactly one reload for the delete -- mount (1) + onPageChange (2) + onDeleted's single
    // refresh (3) file-scoped calls. (A separate 'mediafolder' call also fires at mount.)
    expect(list.mock.calls.filter((c) => c[0] === 'file')).toHaveLength(3)
  })

  it('clamps to the last valid page after a delete strands the current page out of range', async () => {
    const list = makeListMock([
      { data: rows, total: 1 },  // mount (page 0)
      { data: rows, total: 73 }, // onPageChange(2) -- page 2 valid (3 pages)
      { data: [], total: 30 },   // onDeleted's refresh at page 2 -- now out of range (2 pages left)
      { data: rows, total: 30 }, // clamped reload at page 1 (last valid page)
    ])
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onPageChange: (page: number) => void }).onPageChange(2)
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
    // The vendored `ui/button` has no `label` prop -- it renders its text through the default
    // slot -- so `b.props('label')` is always `undefined` and this assertion was structurally
    // unfailable regardless of whether Upload actually rendered. Check the rendered text instead.
    const buttonTexts = w.findAllComponents({ name: 'Button' }).map((b) => b.text())
    expect(buttonTexts.some((text) => text.includes('Upload'))).toBe(false)
  })

  it('shows Upload for a user with write on file', async () => {
    makeListMock([{ data: rows, total: 1 }])
    seedUser({ write: true })
    const w = mountView()
    await flushPromises()
    const buttonTexts = w.findAllComponents({ name: 'Button' }).map((b) => b.text())
    expect(buttonTexts.some((text) => text.includes('Upload'))).toBe(true)
  })

  it('defaults to the active view: no deleted param on the initial load', async () => {
    const list = makeListMock([{ data: rows, total: 1 }])
    mountView()
    await flushPromises()
    const call = list.mock.calls.find((c) => c[0] === 'file')
    expect((call?.[1] as { deleted?: string }).deleted).toBeUndefined()
  })

  it('switching to the trash view lists with deleted:"only"', async () => {
    seedUser({ delete: true })
    const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
    await flushPromises()
    expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ deleted: 'only' }))
  })

  it('hides the active/trash toggle when the user lacks delete permission', async () => {
    makeListMock([{ data: rows, total: 1 }])
    const w = mountView()
    seedUser({ delete: false })
    await flushPromises()
    expect((w.vm as unknown as { showTrashSwitch: boolean }).showTrashSwitch).toBe(false)
  })

  it('shows the active/trash toggle when the user has delete permission', async () => {
    makeListMock([{ data: rows, total: 1 }])
    const w = mountView()
    seedUser({ delete: true })
    await flushPromises()
    expect((w.vm as unknown as { showTrashSwitch: boolean }).showTrashSwitch).toBe(true)
  })

  it('restore in trash view calls filesApi.restore then reloads the list', async () => {
    seedUser({ delete: true })
    const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }, { data: [], total: 0 }])
    const restore = vi.spyOn(filesApi, 'restore').mockResolvedValue()
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
    await flushPromises()
    const fileCallsBefore = list.mock.calls.filter((c) => c[0] === 'file').length
    await (w.vm as unknown as { onRestore: (id: string) => Promise<void> }).onRestore('f1')
    await flushPromises()
    expect(restore).toHaveBeenCalledWith('f1')
    expect(list.mock.calls.filter((c) => c[0] === 'file').length).toBe(fileCallsBefore + 1)
  })

  it('delete-permanently in trash view asks with a top-level danger severity, then calls filesApi.remove(id,{purge:true}) and reloads', async () => {
    seedUser({ delete: true })
    const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }, { data: [], total: 0 }])
    const remove = vi.spyOn(filesApi, 'remove').mockResolvedValue()
    confirmRequire.mockResolvedValueOnce(true)
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
    await flushPromises()
    const fileCallsBefore = list.mock.calls.filter((c) => c[0] === 'file').length
    await (w.vm as unknown as { onPurge: (id: string) => Promise<void> }).onPurge('f1')
    await flushPromises()
    // purgeConfirm(t) carries a top-level severity, matching CollectionListView's onPurge.
    expect(confirmRequire.mock.calls[0][0].severity).toBe('danger')
    expect(remove).toHaveBeenCalledWith('f1', { purge: true })
    expect(list.mock.calls.filter((c) => c[0] === 'file').length).toBe(fileCallsBefore + 1)
  })

  it('surfaces a toast when filesApi.restore fails instead of failing silently', async () => {
    seedUser({ delete: true })
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    vi.spyOn(filesApi, 'restore').mockRejectedValue(new Error('boom'))
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
    await flushPromises()
    await (w.vm as unknown as { onRestore: (id: string) => Promise<void> }).onRestore('f1')
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: 'boom' }))
  })

  it('surfaces a toast when filesApi.remove(purge) fails instead of failing silently', async () => {
    seedUser({ delete: true })
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    vi.spyOn(filesApi, 'remove').mockRejectedValue(new Error('nope'))
    confirmRequire.mockResolvedValueOnce(true)
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
    await flushPromises()
    await (w.vm as unknown as { onPurge: (id: string) => Promise<void> }).onPurge('f1')
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: 'nope' }))
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
    // Folders must be loaded with deep:['parent'] -- the API never returns a flat parentId
    // column, so without this the folder tree/breadcrumbs would never nest correctly.
    expect(list).toHaveBeenCalledWith('mediafolder', expect.objectContaining({ deep: ['parent'] }))
  })

  it('shows folder cards in grid view and folder rows in list view', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('grid')
    await flushPromises()
    expect(w.find('.folder-grid').exists()).toBe(true)

    ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
    await flushPromises()
    expect(w.find('.folder-grid').exists()).toBe(false)
    expect(w.find('.media-list').text()).toContain('A')
  })

  // The "no results" message reads `files` and `visibleFolders`; list view renders folders as
  // rows inside MediaFileList rather than as MediaFolderCards, so an empty `files` array must not
  // trigger the empty-state message while folder rows are still showing in the table.
  it('does not show the empty-state message when list view has folder rows but no files', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: [], total: 0 }, { data: [], total: 0 }], folders)
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
    await flushPromises()
    expect(w.find('.media-list').text()).toContain('A')
    expect(w.find('.empty').exists()).toBe(false)
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
    confirmRequire.mockResolvedValueOnce(true)
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onRemoveFolder: (f: FolderRow) => Promise<void> }).onRemoveFolder(target)
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'warn', summary: 'Folder is not empty' }))
  })

  it('purges a file when the confirmation resolves true', async () => {
    makeListMock([{ data: rows, total: 1 }, { data: [], total: 0 }])
    const remove = vi.spyOn(filesApi, 'remove').mockResolvedValue()
    confirmRequire.mockResolvedValueOnce(true)
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onPurge: (id: string) => Promise<void> }).onPurge('f1')
    await flushPromises()
    expect(remove).toHaveBeenCalledWith('f1', { purge: true })
  })

  it('does not purge when the confirmation resolves false', async () => {
    makeListMock([{ data: rows, total: 1 }])
    const remove = vi.spyOn(filesApi, 'remove').mockResolvedValue()
    confirmRequire.mockResolvedValueOnce(false)
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onPurge: (id: string) => Promise<void> }).onPurge('f1')
    await flushPromises()
    expect(remove).not.toHaveBeenCalled()
  })

  it('asks for folder deletion with a top-level danger severity', async () => {
    makeListMock([{ data: rows, total: 1 }])
    confirmRequire.mockResolvedValueOnce(false)
    const w = mountView()
    await flushPromises()
    const folder: FolderRow = { id: 'd1', name: 'Alpha', parentId: null }
    await (w.vm as unknown as { onRemoveFolder: (f: FolderRow) => Promise<void> }).onRemoveFolder(folder)
    // ConfirmRequest carries severity at the top level, and ConfirmHost reads it to pick the
    // destructive button variant.
    expect(confirmRequire.mock.calls[0][0].severity).toBe('danger')
    expect(confirmRequire.mock.calls[0][0].group).toBeUndefined()
  })

  it('does not remove the folder when the confirmation resolves false', async () => {
    makeListMock([{ data: rows, total: 1 }])
    const remove = vi.spyOn(itemsApi, 'remove').mockResolvedValue()
    confirmRequire.mockResolvedValueOnce(false)
    const w = mountView()
    await flushPromises()
    const folder: FolderRow = { id: 'd1', name: 'Alpha', parentId: null }
    await (w.vm as unknown as { onRemoveFolder: (f: FolderRow) => Promise<void> }).onRemoveFolder(folder)
    await flushPromises()
    expect(remove).not.toHaveBeenCalled()
  })

  it('mounts no per-group confirmation dialog of its own', async () => {
    makeListMock([{ data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    // The store-backed host in AppShell is the only confirmation host in the app, so neither this
    // view nor any of its groups may mount one: a second host bound to that same store renders one
    // confirmation twice, and any other local dialog is a second confirmation surface.
    expect(w.findComponent({ name: 'AlertDialog' }).exists()).toBe(false)
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

  it('renders the trash through the grid when the grid view is selected', async () => {
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('grid')
    ;(w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
    await flushPromises()
    expect(w.find('.media-grid').exists()).toBe(true)
    expect(w.find('.media-trash-list').exists()).toBe(false)
  })

  it('renders the trash through the list when the list view is selected', async () => {
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
    ;(w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
    await flushPromises()
    expect(w.find('.media-list').exists()).toBe(true)
    expect(w.find('.media-trash-list').exists()).toBe(false)
  })

  // The grid branch's restore/purge coverage (icon, accessible-name, type="button" tests above)
  // all default to the grid view and so only ever exercise MediaGrid's #actions copy. MediaGrid
  // and MediaFileList carry two separately-authored `<template #actions>` blocks in this view (see
  // the comment above them for why they aren't merged into one) -- guard the list branch the same
  // way, including a real restore round-trip, so the two copies stay equivalently covered.
  it('gives the list-view trash rows working restore and purge actions', async () => {
    seedUser({ delete: true })
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }, { data: rows, total: 1 }])
    const restore = vi.spyOn(filesApi, 'restore').mockResolvedValue()
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
    ;(w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
    await flushPromises()
    const actionsCell = w.find('.media-list__actions')
    expect(actionsCell.exists()).toBe(true)
    const restoreButton = actionsCell.find('[aria-label="Restore"]')
    const purgeButton = actionsCell.find('[aria-label="Delete permanently"]')
    expect(restoreButton.exists()).toBe(true)
    expect(purgeButton.exists()).toBe(true)

    await restoreButton.trigger('click')
    await flushPromises()
    expect(restore).toHaveBeenCalledWith('f1')
  })

  // Regression test for a stale-computed trap: `MediaFileList` is only `v-else`'d on `view`, not
  // on `mode`, so toggling active<->trash does NOT remount it -- the SAME component instance must
  // react to the trash `#actions` slot appearing across that toggle. The test above switches both
  // `view` and `mode` in the same tick, before any flush, so its `MediaFileList` mounts fresh
  // already in trash mode -- it can't catch a `showActionsColumn` that only re-reads
  // `$slots.actions` when some OTHER, genuinely-reactive prop changes. This test switches to list
  // view first and flushes, THEN toggles into trash on the persisting instance, exercising the
  // exact transition such a stale read would miss.
  it('keeps the actions column live when trash is entered after list view is already showing (persisting instance)', async () => {
    seedUser({ delete: true })
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }, { data: rows, total: 1 }])
    const restore = vi.spyOn(filesApi, 'restore').mockResolvedValue()
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
    await flushPromises()
    expect(w.findComponent({ name: 'MediaFileList' }).exists()).toBe(true)
    // Active + list, no folders -> no actions column at all yet.
    expect(w.find('.media-list__actions').exists()).toBe(false)

    ;(w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
    await flushPromises()
    const actionsCell = w.find('.media-list__actions')
    expect(actionsCell.exists()).toBe(true)
    const restoreButton = actionsCell.find('[aria-label="Restore"]')
    const purgeButton = actionsCell.find('[aria-label="Delete permanently"]')
    expect(restoreButton.exists()).toBe(true)
    expect(purgeButton.exists()).toBe(true)

    await restoreButton.trigger('click')
    await flushPromises()
    expect(restore).toHaveBeenCalledWith('f1')
  })

  it('passes the current folder id to the upload dialog', async () => {
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], [{ id: 'a', name: 'A', parentId: null }])
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { enterFolder: (id: string) => void }).enterFolder('a')
    await flushPromises()
    expect(w.findComponent(MediaUploadDialog).props('folderId')).toBe('a')
  })

  it('moves the dropped payload into the target folder and reloads', async () => {
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onDropOn: (t: string | null, p: { files: string[]; folders: string[] }) => Promise<void> })
      .onDropOn('d1', { files: ['f1'], folders: [] })
    expect(itemsApi.update).toHaveBeenCalledWith('file', 'f1', { folderId: 'd1' })
  })

  it('does nothing when the drop target is the folder already being viewed', async () => {
    const folders: FolderRow[] = [{ id: 'd1', name: 'D1', parentId: null }]
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { enterFolder: (id: string) => void }).enterFolder('d1')
    await flushPromises()
    vi.mocked(itemsApi.update).mockClear()
    await (w.vm as unknown as { onDropOn: (t: string | null, p: { files: string[]; folders: string[] }) => Promise<void> })
      .onDropOn('d1', { files: ['f1'], folders: [] })
    expect(itemsApi.update).not.toHaveBeenCalled()
  })

  it('shows a success toast after a real move and reloads the file list', async () => {
    const list = makeListMock([{ data: rows, total: 1 }, { data: [], total: 0 }])
    const w = mountView()
    await flushPromises()
    const fileCallsBefore = list.mock.calls.filter((c) => c[0] === 'file').length
    await (w.vm as unknown as { onDropOn: (t: string | null, p: { files: string[]; folders: string[] }) => Promise<void> })
      .onDropOn('d1', { files: ['f1'], folders: [] })
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success', summary: 'Moved 1 item(s)' }))
    expect(list.mock.calls.filter((c) => c[0] === 'file').length).toBeGreaterThan(fileCallsBefore)
  })

  it('surfaces a toast and still reloads when a move fails', async () => {
    vi.mocked(itemsApi.update).mockRejectedValueOnce(new Error('boom'))
    const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    const fileCallsBefore = list.mock.calls.filter((c) => c[0] === 'file').length
    await (w.vm as unknown as { onDropOn: (t: string | null, p: { files: string[]; folders: string[] }) => Promise<void> })
      .onDropOn('d1', { files: ['f1'], folders: [] })
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: 'boom' }))
    expect(list.mock.calls.filter((c) => c[0] === 'file').length).toBeGreaterThan(fileCallsBefore)
  })

  it('warns with a skipped-cycle toast when a folder move is refused', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    // Moving 'a' onto itself would form a cycle -- canMoveFolder (via performMove) must refuse it.
    await (w.vm as unknown as { onDropOn: (t: string | null, p: { files: string[]; folders: string[] }) => Promise<void> })
      .onDropOn('a', { files: [], folders: ['a'] })
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({
      severity: 'warn', summary: '1 folder(s) skipped: a folder cannot be moved into itself',
    }))
  })

  // Search mode drops the folder filter (a listed file can live in any folder), so a move
  // initiated from a search result (context menu "Move to…" or the move dialog, both reachable
  // while searching) into the folder currentFolderId happens to point at is NOT a no-op the way it
  // would be while browsing normally -- it must still issue the write.
  it('moves into currentFolderId while a search is active, instead of silently no-opping', async () => {
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onSearchInput: (v: string) => void }).onSearchInput('logo')
    await new Promise((resolve) => setTimeout(resolve, 320))
    await flushPromises()
    // currentFolderId is null (root) and we never navigated, so the target below equals it; while a
    // search is active, a move to the current folder must still issue the write.
    await (w.vm as unknown as { onDropOn: (t: string | null, p: { files: string[]; folders: string[] }) => Promise<void> })
      .onDropOn(null, { files: ['f1'], folders: [] })
    await flushPromises()
    expect(itemsApi.update).toHaveBeenCalledWith('file', 'f1', { folderId: null })
  })

  it('does nothing on a drop with an empty payload', async () => {
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { onDropOn: (t: string | null, p: { files: string[]; folders: string[] }) => Promise<void> })
      .onDropOn('d1', { files: [], folders: [] })
    expect(itemsApi.update).not.toHaveBeenCalled()
  })

  // Permissions: file moves require canWrite('file'); folder moves require canWrite('mediafolder').
  // Without the grant the item must not be draggable at all -- gated through each child's own
  // canMove prop rather than something checked only at drop time.
  it('gates MediaGrid dragging on file write permission', async () => {
    makeListMock([{ data: rows, total: 1 }])
    const w = mountView()
    await flushPromises()
    expect(w.findComponent(MediaGrid).props('canMove')).toBe(false)
    seedUser({ write: true })
    await flushPromises()
    expect(w.findComponent(MediaGrid).props('canMove')).toBe(true)
  })

  it('gates MediaFolderCards dragging on mediafolder write permission', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    expect(w.findComponent(MediaFolderCards).props('canMove')).toBe(false)
    seedUser({ write: true }, 'mediafolder')
    await flushPromises()
    expect(w.findComponent(MediaFolderCards).props('canMove')).toBe(true)
  })

  it('wires MediaFolderCards\' dropOn emit to onDropOn', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    w.findComponent(MediaFolderCards).vm.$emit('dropOn', 'a', { files: ['f1'], folders: [] })
    await flushPromises()
    expect(itemsApi.update).toHaveBeenCalledWith('file', 'f1', { folderId: 'a' })
  })

  // List view's MediaFileList must be gated the same way as the grid's MediaGrid/MediaFolderCards
  // -- same permissions, same components, just a different presentation.
  it('gates MediaFileList row dragging on write permission (files and folders separately)', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
    await flushPromises()
    expect(w.findComponent(MediaFileList).props('canMoveFiles')).toBe(false)
    expect(w.findComponent(MediaFileList).props('canMoveFolders')).toBe(false)
    seedUser({ write: true })
    await flushPromises()
    expect(w.findComponent(MediaFileList).props('canMoveFiles')).toBe(true)
    expect(w.findComponent(MediaFileList).props('canMoveFolders')).toBe(false)
    seedUser({ write: true }, 'mediafolder')
    await flushPromises()
    expect(w.findComponent(MediaFileList).props('canMoveFolders')).toBe(true)
  })

  it('wires MediaFileList\'s dropOn emit to onDropOn', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
    await flushPromises()
    w.findComponent(MediaFileList).vm.$emit('dropOn', 'a', { files: ['f1'], folders: [] })
    await flushPromises()
    expect(itemsApi.update).toHaveBeenCalledWith('file', 'f1', { folderId: 'a' })
  })

  // Breadcrumb segments are drop targets too: dropping onto an ancestor
  // crumb moves the payload there via the same onDropOn path, exercised here through a real DOM
  // drop event rather than a $vm call, to also cover the template wiring itself.
  it('drops a payload onto the root breadcrumb and moves it there', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { enterFolder: (id: string) => void }).enterFolder('a')
    await flushPromises()
    const dataTransfer = {
      types: [DRAG_MIME],
      getData: (t: string) => (t === DRAG_MIME ? serializeMovePayload({ files: ['f1'], folders: [] }) : ''),
      dropEffect: '',
    }
    const rootCrumb = w.find('.media-crumb__link')
    await rootCrumb.trigger('drop', { dataTransfer })
    await flushPromises()
    expect(itemsApi.update).toHaveBeenCalledWith('file', 'f1', { folderId: null })
  })

  it('does not treat a foreign (non-media) drop on the breadcrumb as a move', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { enterFolder: (id: string) => void }).enterFolder('a')
    await flushPromises()
    const dataTransfer = { types: ['text/plain'], getData: () => 'hello', dropEffect: '' }
    const rootCrumb = w.find('.media-crumb__link')
    await rootCrumb.trigger('drop', { dataTransfer })
    await flushPromises()
    expect(itemsApi.update).not.toHaveBeenCalled()
  })

  // A drag can end without ever reaching a drop (Esc, or a drop somewhere that isn't a
  // registered target) -- nothing else resets the breadcrumb highlight in that case, and the
  // drag may have started on a different component's element (a MediaGrid tile or a
  // MediaFolderCards card), so this must be caught at the document level, not scoped to the
  // crumb's own listeners (crumb buttons are never drag sources themselves).
  it('clears the breadcrumb drop-highlight when a drag ends anywhere (abandoned drag)', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { enterFolder: (id: string) => void }).enterFolder('a')
    await flushPromises()
    const rootCrumb = w.find('.media-crumb__link')
    const dataTransfer = { types: [DRAG_MIME], getData: () => '', dropEffect: '' }
    await rootCrumb.trigger('dragover', { dataTransfer })
    expect(rootCrumb.attributes('data-dropping')).toBe('true')
    document.dispatchEvent(new Event('dragend'))
    await flushPromises()
    expect(rootCrumb.attributes('data-dropping')).toBeUndefined()
  })

  // The root crumb navigating to the root you're already viewing is a no-op that looks broken --
  // at the root it must render as inert text (the same treatment already given to the current
  // folder's own crumb), not as a clickable button.
  it('renders the root crumb as static text, not a link, when already at the root', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    const rootCrumb = w.find('.media-crumb__current')
    expect(rootCrumb.exists()).toBe(true)
    expect(rootCrumb.element.tagName).toBe('SPAN')
    expect(rootCrumb.text()).toBe('Media Library')
    expect(w.find('.media-crumb__link').exists()).toBe(false)
  })

  // Once inside a folder, the root crumb is the way back up and stays a real button.
  it('keeps the root crumb as a clickable link that returns to the root when inside a folder', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    await (w.vm as unknown as { enterFolder: (id: string) => void }).enterFolder('a')
    await flushPromises()
    const rootCrumb = w.find('.media-crumb__link')
    expect(rootCrumb.exists()).toBe(true)
    expect(rootCrumb.element.tagName).toBe('BUTTON')
    await rootCrumb.trigger('click')
    await flushPromises()
    expect((w.vm as unknown as { currentFolderId: string | null }).currentFolderId).toBe(null)
  })

  // The move-to dialog's submit is wired to the existing move path. Exercise the wiring directly
  // through the exposed state, the same way createOpen/renameTarget are driven elsewhere here.
  it('routes the move-to dialog\'s submit into the existing move path', async () => {
    const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
    makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
    const w = mountView()
    await flushPromises()
    ;(w.vm as unknown as { movePayload: MovePayload }).movePayload = { files: ['f1'], folders: [] }
    await flushPromises()
    w.findComponent(MediaMoveDialog).vm.$emit('submit', 'a')
    await flushPromises()
    expect(itemsApi.update).toHaveBeenCalledWith('file', 'f1', { folderId: 'a' })
  })

  // The context menu's "Move to…" entry is a UI trigger for the move dialog.
  describe('context menu wiring', () => {
    it('sets movePayload and opens the move dialog when onRequestMove runs', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { onRequestMove: (p: MovePayload) => void })
        .onRequestMove({ files: ['f1'], folders: [] })
      await flushPromises()
      expect((w.vm as unknown as { movePayload: MovePayload }).movePayload).toEqual({ files: ['f1'], folders: [] })
      expect((w.vm as unknown as { moveDialogOpen: boolean }).moveDialogOpen).toBe(true)
    })

    it('routes MediaGrid\'s requestMove emit into onRequestMove', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      w.findComponent(MediaGrid).vm.$emit('requestMove', { files: ['f1'], folders: [] })
      await flushPromises()
      expect((w.vm as unknown as { moveDialogOpen: boolean }).moveDialogOpen).toBe(true)
    })

    it('routes MediaFolderCards\' requestMove emit into onRequestMove', async () => {
      const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
      makeListMock([{ data: rows, total: 1 }], folders)
      const w = mountView()
      await flushPromises()
      w.findComponent(MediaFolderCards).vm.$emit('requestMove', { files: [], folders: ['a'] })
      await flushPromises()
      expect((w.vm as unknown as { movePayload: MovePayload }).movePayload).toEqual({ files: [], folders: ['a'] })
    })

    it('routes MediaFileList\'s requestMove emit into onRequestMove', async () => {
      const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
      makeListMock([{ data: rows, total: 1 }], folders)
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
      await flushPromises()
      w.findComponent(MediaFileList).vm.$emit('requestMove', { files: ['f1'], folders: [] })
      await flushPromises()
      expect((w.vm as unknown as { movePayload: MovePayload }).movePayload).toEqual({ files: ['f1'], folders: [] })
    })

    it('deletes a file when onRemoveFile is confirmed, then reloads', async () => {
      const list = makeListMock([{ data: rows, total: 1 }, { data: [], total: 0 }])
      const remove = vi.spyOn(filesApi, 'remove').mockResolvedValue()
      confirmRequire.mockResolvedValueOnce(true)
      const w = mountView()
      await flushPromises()
      const fileCallsBefore = list.mock.calls.filter((c) => c[0] === 'file').length
      await (w.vm as unknown as { onRemoveFile: (id: string) => Promise<void> }).onRemoveFile('f1')
      await flushPromises()
      expect(remove).toHaveBeenCalledWith('f1')
      expect(list.mock.calls.filter((c) => c[0] === 'file').length).toBe(fileCallsBefore + 1)
    })

    it('does not delete a file when the confirmation is declined', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const remove = vi.spyOn(filesApi, 'remove').mockResolvedValue()
      confirmRequire.mockResolvedValueOnce(false)
      const w = mountView()
      await flushPromises()
      await (w.vm as unknown as { onRemoveFile: (id: string) => Promise<void> }).onRemoveFile('f1')
      await flushPromises()
      expect(remove).not.toHaveBeenCalled()
    })

    it('surfaces a toast when onRemoveFile fails instead of failing silently', async () => {
      makeListMock([{ data: rows, total: 1 }])
      vi.spyOn(filesApi, 'remove').mockRejectedValue(new Error('nope'))
      confirmRequire.mockResolvedValueOnce(true)
      const w = mountView()
      await flushPromises()
      await (w.vm as unknown as { onRemoveFile: (id: string) => Promise<void> }).onRemoveFile('f1')
      await flushPromises()
      expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: 'nope' }))
    })

    it('closes the detail dialog when the file being deleted is the one currently open', async () => {
      makeListMock([{ data: rows, total: 1 }, { data: [], total: 0 }])
      vi.spyOn(filesApi, 'remove').mockResolvedValue()
      confirmRequire.mockResolvedValueOnce(true)
      const w = mountView()
      await flushPromises()
      await w.find('.media-tile').trigger('click')
      expect(w.findComponent({ name: 'MediaDetailDialog' }).props('file')).toEqual(rows[0])
      await (w.vm as unknown as { onRemoveFile: (id: string) => Promise<void> }).onRemoveFile('f1')
      await flushPromises()
      expect(w.findComponent({ name: 'MediaDetailDialog' }).props('file')).toBeNull()
    })

    // Permissions: MediaGrid's Delete entry requires canDelete('file'); MediaFolderCards'/
    // MediaFileList's Rename/Delete entries require canManageFolders/canDeleteFolders
    // respectively -- same reasoning as the drag-and-drop canMove props already gated this way.
    it('gates MediaGrid\'s Delete entry on file delete permission', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      expect(w.findComponent(MediaGrid).props('canDelete')).toBe(false)
      seedUser({ delete: true })
      await flushPromises()
      expect(w.findComponent(MediaGrid).props('canDelete')).toBe(true)
    })

    it('gates MediaFolderCards\' Rename/Delete entries on their own mediafolder grants', async () => {
      const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
      makeListMock([{ data: rows, total: 1 }], folders)
      const w = mountView()
      await flushPromises()
      expect(w.findComponent(MediaFolderCards).props('canRename')).toBe(false)
      expect(w.findComponent(MediaFolderCards).props('canDelete')).toBe(false)
      seedUser({ write: true }, 'mediafolder')
      await flushPromises()
      expect(w.findComponent(MediaFolderCards).props('canRename')).toBe(true)
      expect(w.findComponent(MediaFolderCards).props('canDelete')).toBe(false)
    })

    it('gates MediaFileList\'s Rename/Delete entries (files and folders separately)', async () => {
      const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
      makeListMock([{ data: rows, total: 1 }], folders)
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
      await flushPromises()
      expect(w.findComponent(MediaFileList).props('canDeleteFiles')).toBe(false)
      expect(w.findComponent(MediaFileList).props('canRenameFolders')).toBe(false)
      expect(w.findComponent(MediaFileList).props('canDeleteFolders')).toBe(false)
      seedUser({ delete: true })
      await flushPromises()
      expect(w.findComponent(MediaFileList).props('canDeleteFiles')).toBe(true)
    })

    // Trash tiles already have restore/purge as slot actions -- the view must forward trashMode
    // so MediaGrid/MediaFileList suppress their own context menu there (see each component's own
    // "does not open a menu at all when trashMode is true" test for the actual suppression).
    it('forwards trashMode to MediaGrid and MediaFileList only while browsing the trash', async () => {
      seedUser({ delete: true })
      makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      // Default view is grid: MediaGrid's own trashMode binding must be pinned here too, not just
      // MediaFileList's.
      expect(w.findComponent(MediaGrid).props('trashMode')).toBe(false)
      ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
      await flushPromises()
      expect(w.findComponent(MediaGrid).exists()).toBe(false)
      expect(w.findComponent(MediaFileList).props('trashMode')).toBe(false)
      ;(w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
      await flushPromises()
      expect(w.findComponent(MediaFileList).props('trashMode')).toBe(true)
      ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('grid')
      await flushPromises()
      expect(w.findComponent(MediaGrid).props('trashMode')).toBe(true)
    })

    // Empty-space menu: right-clicking the library body (not an item) offers New folder/Upload,
    // gated the same way as the header's own buttons.
    it('offers New folder and Upload from the empty-space menu when both grants are present', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const auth = useAuthStore()
      auth.user = {
        id: 'u1', isSuperAdmin: false,
        permissions: { file: { read: true, write: true, delete: false }, mediafolder: { read: true, write: true, delete: false } },
      } as CurrentUser
      const w = mountView()
      await flushPromises()
      await w.find('.media-body').trigger('contextmenu')
      await flushPromises()
      expect(w.find('[data-test="menu-new-folder"]').exists()).toBe(true)
      expect(w.find('[data-test="menu-upload"]').exists()).toBe(true)
    })

    it('hides New folder and Upload from the empty-space menu without their grants', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const w = mountView()
      seedUser({ write: false }, 'file')
      await flushPromises()
      await w.find('.media-body').trigger('contextmenu')
      await flushPromises()
      expect(w.find('[data-test="menu-new-folder"]').exists()).toBe(false)
      expect(w.find('[data-test="menu-upload"]').exists()).toBe(false)
    })

    it('opens the create-folder dialog when New folder is selected from the empty-space menu', async () => {
      makeListMock([{ data: rows, total: 1 }])
      seedUser({ write: true }, 'mediafolder')
      const w = mountView()
      await flushPromises()
      await w.find('.media-body').trigger('contextmenu')
      await flushPromises()
      await w.find('[data-test="menu-new-folder"]').trigger('click')
      expect((w.vm as unknown as { createOpen: boolean }).createOpen).toBe(true)
    })

    it('opens the upload dialog when Upload is selected from the empty-space menu', async () => {
      makeListMock([{ data: rows, total: 1 }])
      seedUser({ write: true }, 'file')
      const w = mountView()
      await flushPromises()
      await w.find('.media-body').trigger('contextmenu')
      await flushPromises()
      await w.find('[data-test="menu-upload"]').trigger('click')
      expect(w.findComponent(MediaUploadDialog).props('visible')).toBe(true)
    })

    // A right-click that lands ON a tile must not ALSO open the empty-space menu underneath it --
    // each item-level menu stops propagation precisely so the two don't stack.
    it('does not also open the empty-space menu when a tile is right-clicked', async () => {
      makeListMock([{ data: rows, total: 1 }])
      seedUser({ write: true }, 'mediafolder')
      const w = mountView()
      await flushPromises()
      await w.find('.media-tile').trigger('contextmenu')
      await flushPromises()
      expect(w.find('[data-test="menu-new-folder"]').exists()).toBe(false)
    })

    // The maintainer's whole point in adding the card outline was so the user can SEE where the
    // right-click menu above actually works -- so the outline must land on the exact DOM node that
    // is the context-menu trigger, not a wrapper or sibling around it. `[data-slot="context-menu-
    // trigger"]` is stamped by the vendored ContextMenuTrigger itself (see ui/context-menu/
    // ContextMenuTrigger.vue) onto its own root element, so querying by that selector and reading
    // classes off the SAME element pins identity rather than merely checking the two facts
    // separately (which would stay green even if the card styling drifted onto some other node).
    it('draws the card styling on the exact element that is the context-menu trigger', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      const trigger = w.find('[data-slot="context-menu-trigger"]')
      expect(trigger.exists()).toBe(true)
      expect(trigger.classes()).toContain('media-body')
      expect(trigger.classes()).toEqual(
        expect.arrayContaining(['rounded-xl', 'border', 'border-border', 'bg-muted']))
    })
  })

  // Batch selection wiring and the clear-on-listing-change requirement. A selection that
  // survives a listing change would let a later batch move act on items the user can't see --
  // and canMoveFolder (mediaMove.ts) returns true for a source id absent from its folders
  // list, so a stale selection assembled before a reload could pass the cycle guard on a folder
  // that has since disappeared from the loaded set. Clearing on every listing change keeps that
  // unreachable.
  describe('batch selection', () => {
    it('wires MediaGrid\'s toggleSelect emit into the shared selection', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      w.findComponent(MediaGrid).vm.$emit('toggleSelect', 'file', 'f1')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: ['f1'], folders: [] })
    })

    it('wires MediaFolderCards\' toggleSelect emit into the shared selection', async () => {
      const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
      makeListMock([{ data: rows, total: 1 }], folders)
      const w = mountView()
      await flushPromises()
      w.findComponent(MediaFolderCards).vm.$emit('toggleSelect', 'folder', 'a')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: ['a'] })
    })

    it('wires MediaFileList\'s toggleSelect emit into the shared selection', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { onViewToggle: (v: unknown) => void }).onViewToggle('list')
      await flushPromises()
      w.findComponent(MediaFileList).vm.$emit('toggleSelect', 'file', 'f1')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: ['f1'], folders: [] })
    })

    it('shows the selection toolbar once the selection is non-empty, and hides it when empty', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      // MediaSelectionToolbar is mounted regardless (its own root `v-if="count > 0"` governs
      // whether it renders anything), so findComponent always "exists" -- assert its rendered DOM.
      expect(w.find('.media-selection-toolbar').exists()).toBe(false)
      w.findComponent(MediaGrid).vm.$emit('toggleSelect', 'file', 'f1')
      await flushPromises()
      expect(w.find('.media-selection-toolbar').exists()).toBe(true)
      expect(w.findComponent(MediaSelectionToolbar).props('selection')).toEqual({ files: ['f1'], folders: [] })
    })

    it('opens the move dialog with the whole selection when the toolbar requests a move', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      w.findComponent(MediaSelectionToolbar).vm.$emit('requestMove', { files: ['f1'], folders: [] })
      await flushPromises()
      expect((w.vm as unknown as { movePayload: MovePayload }).movePayload).toEqual({ files: ['f1'], folders: [] })
      expect((w.vm as unknown as { moveDialogOpen: boolean }).moveDialogOpen).toBe(true)
    })

    it('clears the selection when the toolbar\'s clear button fires', async () => {
      makeListMock([{ data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      w.findComponent(MediaSelectionToolbar).vm.$emit('clear')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
    })

    it('passes canWrite(file)/canWrite(mediafolder) through to the toolbar', async () => {
      makeListMock([{ data: rows, total: 1 }])
      seedUser({ write: true }, 'file')
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      const toolbar = w.findComponent(MediaSelectionToolbar)
      expect(toolbar.props('canMoveFiles')).toBe(true)
      expect(toolbar.props('canMoveFolders')).toBe(false)
    })

    it('clears the selection when entering a folder', async () => {
      const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
      makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      await (w.vm as unknown as { enterFolder: (id: string) => void }).enterFolder('a')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
    })

    it('clears the selection when navigating via a breadcrumb', async () => {
      const folders: FolderRow[] = [{ id: 'a', name: 'A', parentId: null }]
      makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }], folders)
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      await (w.vm as unknown as { goToBreadcrumb: (id: string | null) => void }).goToBreadcrumb(null)
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
    })

    it('clears the selection when switching mode', async () => {
      seedUser({ delete: true })
      makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      ;(w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
    })

    it('clears the selection when searching', async () => {
      makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      ;(w.vm as unknown as { onSearchInput: (v: string) => void }).onSearchInput('logo')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
    })

    it('clears the selection when the page changes', async () => {
      makeListMock([{ data: rows, total: 100 }, { data: rows, total: 100 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      ;(w.vm as unknown as { onPageChange: (page: number) => void }).onPageChange(1)
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
    })

    it('clears the selection when the page size changes', async () => {
      makeListMock([{ data: rows, total: 100 }, { data: rows, total: 100 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      ;(w.vm as unknown as { onPageSizeChange: (n: number) => void }).onPageSizeChange(48)
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
    })

    // Blocker 2 (final review): MediaUploadDialog's `done` handler resets `page` to 0 via
    // reload() exactly like search/page/mode/type/sort already do, yet it was the one
    // listing-changing path with no clearSelection() call. Select on page >= 2, upload, and
    // "Move to..." would silently act on items not on the current page.
    it('clears the selection when the upload dialog finishes', async () => {
      makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      w.findComponent(MediaUploadDialog).vm.$emit('done')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
    })

    // Trash mode: a batch move on soft-deleted items is nonsensical, and MediaMoveDialog is
    // mounted unconditionally regardless of mode -- refuse at the true source (the toggle
    // handler itself) rather than relying only on the toolbar never appearing in practice
    // because `mode` switches always clear the selection first.
    it('refuses to add to the selection while browsing the trash', async () => {
      seedUser({ delete: true })
      makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
      await flushPromises()
      w.findComponent(MediaGrid).vm.$emit('toggleSelect', 'file', 'f1')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
    })

    // onType/onSort also reload `files.value` just like search/page/mode
    // do -- a selection surviving one of those is a SILENT SUCCESS hazard, not just staleness:
    // "3 selected" would still show after switching the type filter to something that hides all
    // three, and Move to… would then move items the user can't see without any error at
    // all. Both must clear the same way the other four listing-change triggers already do.
    it('clears the selection when the type filter changes', async () => {
      const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      await (w.vm as unknown as { onType: (t: string) => void }).onType('image')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
      // onType still does its own job -- clearing must not have replaced or skipped the reload.
      expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({
        filter: { contentType: { op: '_starts_with', value: 'image/' }, folderId: { op: '_null', value: 'true' } },
      }))
    })

    it('clears the selection when the sort order changes', async () => {
      const list = makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      await flushPromises()
      await (w.vm as unknown as { onSort: (s: string) => void }).onSort('name')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
      expect(list).toHaveBeenLastCalledWith('file', expect.objectContaining({ sort: 'fileName' }))
    })

    // onMoveSubmit is the move-DIALOG path (both the toolbar's batch
    // move and a single-item context-menu move funnel through it) as distinct from onDropOn's
    // raw drag-and-drop path. After a submitted move, the selection's ids may already be missing
    // from the current listing (they were just moved elsewhere) -- clear here specifically, not inside
    // onDropOn's own finally (which also serves plain drags/context-menu moves where clearing an
    // unrelated selection would be wrong).
    it('clears the selection when the move dialog is submitted', async () => {
      makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }])
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: [] }
      ;(w.vm as unknown as { movePayload: MovePayload }).movePayload = { files: ['f1'], folders: [] }
      await flushPromises()
      ;(w.vm as unknown as { onMoveSubmit: (t: string | null) => void }).onMoveSubmit('a')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: [], folders: [] })
      expect(itemsApi.update).toHaveBeenCalledWith('file', 'f1', { folderId: 'a' })
    })

    // A ghost id (one just deleted/purged/removed) makes performMove
    // throw AFTER its sibling writes have already settled (mediaMoveActions.ts's
    // Promise.allSettled + "first rejection wins" re-throw), so a later batch move on a selection
    // containing that ghost id would report "Move failed" even though every other file in the
    // same batch was actually moved -- and, worse, the batch would stay stuck failing forever
    // since nothing removed the ghost id. Surgical removal (not a wholesale clearSelection()) is
    // used here because the user's OTHER selected items are still perfectly valid -- each test
    // below asserts a sibling id survives, not just that the acted-upon id is gone.
    it('removes just the deleted file from the selection when onDeleted fires (a sibling id survives)', async () => {
      makeListMock([{ data: rows, total: 1 }, { data: [], total: 0 }])
      const w = mountView()
      await flushPromises()
      // Opens the detail dialog on f1, which is what sets `selected.value` that onDeleted reads.
      await w.find('.media-tile').trigger('click')
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1', 'other'], folders: [] }
      await flushPromises()
      ;(w.vm as unknown as { onDeleted: () => void }).onDeleted()
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: ['other'], folders: [] })
    })

    it('removes just the restored file from the selection when onRestore succeeds (a sibling id survives)', async () => {
      seedUser({ delete: true })
      makeListMock([{ data: rows, total: 1 }, { data: rows, total: 1 }, { data: [], total: 0 }])
      vi.spyOn(filesApi, 'restore').mockResolvedValue()
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { setMode: (m: 'active' | 'trash') => void }).setMode('trash')
      await flushPromises()
      // Seeded directly (bypassing the normal toggle path, which already refuses additions in
      // trash mode) so this test isolates onRestore's own removal behaviour.
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1', 'other'], folders: [] }
      await flushPromises()
      await (w.vm as unknown as { onRestore: (id: string) => Promise<void> }).onRestore('f1')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: ['other'], folders: [] })
    })

    it('removes just the purged file from the selection when onPurge succeeds (a sibling id survives)', async () => {
      seedUser({ delete: true })
      makeListMock([{ data: rows, total: 1 }, { data: [], total: 0 }])
      vi.spyOn(filesApi, 'remove').mockResolvedValue()
      confirmRequire.mockResolvedValueOnce(true)
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1', 'other'], folders: [] }
      await flushPromises()
      await (w.vm as unknown as { onPurge: (id: string) => Promise<void> }).onPurge('f1')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: ['other'], folders: [] })
    })

    it('removes just the removed folder from the selection when onRemoveFolder succeeds (a sibling id survives)', async () => {
      const target: FolderRow = { id: 'a', name: 'A', parentId: null }
      makeListMock([{ data: rows, total: 1 }], [target])
      vi.spyOn(itemsApi, 'remove').mockResolvedValue()
      confirmRequire.mockResolvedValueOnce(true)
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1'], folders: ['a', 'other'] }
      await flushPromises()
      await (w.vm as unknown as { onRemoveFolder: (f: FolderRow) => Promise<void> }).onRemoveFolder(target)
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: ['f1'], folders: ['other'] })
    })

    it('removes just the removed file from the selection when onRemoveFile succeeds (a sibling id survives)', async () => {
      makeListMock([{ data: rows, total: 1 }, { data: [], total: 0 }])
      vi.spyOn(filesApi, 'remove').mockResolvedValue()
      confirmRequire.mockResolvedValueOnce(true)
      const w = mountView()
      await flushPromises()
      ;(w.vm as unknown as { selection: MovePayload }).selection = { files: ['f1', 'other'], folders: [] }
      await flushPromises()
      await (w.vm as unknown as { onRemoveFile: (id: string) => Promise<void> }).onRemoveFile('f1')
      await flushPromises()
      expect((w.vm as unknown as { selection: MovePayload }).selection).toEqual({ files: ['other'], folders: [] })
    })
  })
})
