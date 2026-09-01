import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { reactive } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import CollectionListView from './CollectionListView.vue'
import FilterBuilder from '@/components/data/FilterBuilder.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { itemsApi } from '../api/itemsApi'
import { schemaApi } from '../api/schemaApi'
import { LANGUAGE_COLLECTION } from '../lib/frameworkCollections'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: {
    en: {
      collectionList: {
        count: '{n} items', new: 'New', searchPlaceholder: 'Search…',
        range: 'Showing {from}–{to} of {total}', active: 'Active', trash: 'Trash',
        edit: 'Edit', view: 'View', delete: 'Delete', restore: 'Restore', purge: 'Delete permanently',
        empty: 'No records', notFound: 'Collection not found',
        noAccess: "You don't have access to this collection",
        trashNotice: 'You are viewing the trash. Items here are hidden from the site and API; restore them or delete them permanently.',
        deletedAt: 'Deleted at', emptyTrash: 'Trash is empty',
      },
      confirm: {
        softDeleteHeader: 'Move to trash',
        softDeleteMessage: 'Move this item to trash? You can restore it later.',
        hardDeleteHeader: 'Confirm delete',
        hardDeleteMessage: 'Delete this item? This cannot be undone.',
        purgeHeader: 'Delete permanently',
        purgeMessage: 'Permanently delete this item? This cannot be undone.',
      },
      filterBuilder: {
        addCondition: 'Add condition', clear: 'Clear conditions', apply: 'Search',
        field: 'Field', operator: 'Operator', value: 'Value', remove: 'Remove condition',
        opEq: 'equals', opNeq: 'does not equal', opContains: 'contains',
      },
      common: {
        sortAscending: 'Sort ascending', sortDescending: 'Sort descending',
        clearSort: 'Clear sorting', previous: 'Previous page', next: 'Next page',
        confirmDefaultHeader: 'Please confirm', confirmAccept: 'Confirm', confirmReject: 'Cancel',
        loadFailed: 'Load failed', actionFailed: 'Action failed',
        columns: 'Columns', rowsPerPage: 'Rows per page',
      },
    },
  },
})

function mountView() {
  return mount(CollectionListView, { global: { plugins: [i18n] } })
}

const pushMock = vi.fn()
// Reactive route so tests can drive collection switches (watch(name)). Read lazily
// inside useRoute()/setup (test time), so declaration order vs. the hoisted mock is safe.
const mockRoute = reactive({ params: { name: 'article' } })
vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
  useRouter: () => ({ push: pushMock }),
}))
vi.mock('../api/itemsApi', () => ({
  itemsApi: { list: vi.fn(), remove: vi.fn(), restore: vi.fn() },
}))
vi.mock('../api/schemaApi', () => ({
  schemaApi: { getAll: vi.fn(), get: vi.fn() },
}))
// Confirmation is asked through the store-backed composable now (see composables/useConfirm) —
// no dialog is mounted inside this view (ConfirmHost lives once at the AppShell level), so tests
// only need to control what require() resolves to and inspect what it was called with.
const confirmRequire = vi.fn()
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))

function seedSchema() {
  const schema = useSchemaStore()
  schema.collections = [{
    name: 'article', label: 'Article', defaultDisplayField: 'status',
    fields: [{ name: 'status', label: 'Status', interface: 'select', required: false, searchable: false,
      sortable: true, readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
      options: [{ value: 'draft', label: 'Draft' }] }],
    relations: [],
  }]
  schema.loaded = true // pre-seeded directly (not via schemaApi); mark loaded so loadItems' schema.load() is a no-op
}

function seedSoftSchema() {
  const schema = useSchemaStore()
  schema.collections = [{
    name: 'article', label: 'Article', defaultDisplayField: 'status', softDelete: true,
    fields: [{ name: 'status', label: 'Status', interface: 'select', required: false, searchable: false,
      sortable: true, readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
      options: [{ value: 'draft', label: 'Draft' }] }],
    relations: [],
  }]
  schema.loaded = true
}

function seedLanguage() {
  const lang = useLanguageStore()
  lang.load = vi.fn().mockResolvedValue(undefined)
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  return lang
}

describe('CollectionListView', () => {
  beforeEach(() => {
    setActivePinia(createPinia()); vi.clearAllMocks(); pushMock.mockClear()
    confirmRequire.mockReset(); confirmRequire.mockResolvedValue(true)
    mockRoute.params.name = 'article'
  })
  afterEach(() => { vi.useRealTimers() })

  it('loads items on mount for a readable collection', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ status: 'draft' }], total: 1 })
    mountView()
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article', { page: 0, rows: 25, sort: undefined, filter: undefined, locale: 'en', deleted: undefined })
  })

  it('deep link / hard refresh: loads schema itself (not pre-seeded) then loads items', async () => {
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    // schema store starts empty/unloaded here -- no seedSchema() call, unlike every other test in this file.
    let resolveSchema!: (v: unknown) => void
    const pending = new Promise((resolve) => { resolveSchema = resolve })
    vi.mocked(schemaApi.getAll).mockReturnValue(pending as ReturnType<typeof schemaApi.getAll>)
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ status: 'draft' }], total: 1 })
    const wrapper = mountView()
    await flushPromises()
    // Schema hasn't arrived yet: view must not have bailed out permanently.
    expect(itemsApi.list).not.toHaveBeenCalled()
    resolveSchema([{
      name: 'article', label: 'Article', defaultDisplayField: 'status',
      fields: [{ name: 'status', label: 'Status', interface: 'select', required: false, searchable: false,
        sortable: true, readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
        options: [{ value: 'draft', label: 'Draft' }] }],
      relations: [],
    }])
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article', { page: 0, rows: 25, sort: undefined, filter: undefined, locale: 'en', deleted: undefined })
    expect(wrapper.text()).not.toContain('Collection not found')
    expect(wrapper.text()).not.toContain("don't have access")
  })

  it('renders a translatable column from translations[locale] instead of "—"', async () => {
    const schema = useSchemaStore()
    schema.collections = [{
      name: 'article', label: 'Article', defaultDisplayField: 'title',
      fields: [{ name: 'title', label: 'Title', interface: 'text', required: false, searchable: false,
        sortable: true, readOnly: false, hidden: false, translatable: true, sort: 0, isSystem: false }],
      relations: [],
    }]
    schema.loaded = true
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({
      data: [{ id: '1', translations: { en: { title: 'Hello' } } }],
      total: 1,
    })
    const wrapper = mountView()
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      cellValue: (row: Record<string, unknown>, field: unknown) => unknown
      rows: Record<string, unknown>[]
    }
    const titleField = schema.collections[0].fields[0]
    expect(vm.cellValue(vm.rows[0], titleField)).toBe('Hello')
  })

  it('shows a permission message and makes no API call when not readable', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: false, permissions: {} }
    const wrapper = mountView()
    await flushPromises()
    expect(itemsApi.list).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain("don't have access")
  })

  it('shows an error message when the list load fails', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockRejectedValue(new Error('Server error.'))
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.text()).toContain('Server error.')
  })

  it('translates the table state into the backend sort token', async () => {
    seedSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mountView()
    await flushPromises()

    await (w.vm as any).onTableState({ sort: [{ id: 'title', desc: true }], page: 0, pageSize: 25 })
    await flushPromises()
    expect(vi.mocked(itemsApi.list)).toHaveBeenLastCalledWith('article', expect.objectContaining({ sort: '-title' }))

    await (w.vm as any).onTableState({ sort: [{ id: 'title', desc: false }], page: 0, pageSize: 25 })
    await flushPromises()
    expect(vi.mocked(itemsApi.list)).toHaveBeenLastCalledWith('article', expect.objectContaining({ sort: 'title' }))

    await (w.vm as any).onTableState({ sort: [], page: 0, pageSize: 25 })
    await flushPromises()
    expect(vi.mocked(itemsApi.list)).toHaveBeenLastCalledWith('article', expect.objectContaining({ sort: undefined }))
  })

  it('onPageChange moves to the requested page and reloads while keeping the active sort', async () => {
    seedSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 100 })
    const w = mountView()
    await flushPromises()
    const vm = w.vm as any
    await vm.onTableState({ sort: [{ id: 'status', desc: true }], page: 0, pageSize: 25 })
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    vm.onPageChange(2)
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article', expect.objectContaining({ page: 2, sort: '-status' }))
  })

  it('onPageSizeChange resets to page 0 and reloads at the new page size', async () => {
    seedSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 100 })
    const w = mountView()
    await flushPromises()
    const vm = w.vm as any
    vm.onPageChange(3)
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    vm.onPageSizeChange(50)
    await flushPromises()
    // page reset to 0 -- an offset computed against the OLD page size is meaningless here
    expect(itemsApi.list).toHaveBeenCalledWith('article',
      expect.objectContaining({ page: 0, rows: 50 }))
    expect(vm.tableState).toEqual({ sort: [], page: 0, pageSize: 50 })
  })

  it('sends the applied filter spec to the API', async () => {
    seedSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mountView()
    await flushPromises()
    await (w.vm as any).onFilterApply({ title: { op: '_contains', value: 'hello' } })
    await flushPromises()
    expect(vi.mocked(itemsApi.list)).toHaveBeenLastCalledWith('article', expect.objectContaining({
      filter: { title: { op: '_contains', value: 'hello' } },
      page: 0, // a new filter must return to the first page
    }))
  })

  it('onEdit navigates to the item edit page', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const wrapper = mountView()
    await flushPromises()
    const vm: any = wrapper.vm
    vm.onEdit({ id: '42' })
    expect(pushMock).toHaveBeenCalledWith({ name: 'collection-item', params: { name: 'article', id: '42' } })
  })

  it('read-only viewer sees a view action instead of edit, and onEdit still navigates', async () => {
    seedSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: false,
      permissions: { article: { read: true, write: false, delete: false } } }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '42', status: 'draft' }], total: 1 })
    const w = mountView()
    await flushPromises()
    expect(w.find('[data-testid="row-view"]').exists()).toBe(true)
    expect(w.find('[data-testid="row-edit"]').exists()).toBe(false)
    const vm: any = w.vm
    vm.onEdit({ id: '42' })
    expect(pushMock).toHaveBeenCalledWith({ name: 'collection-item', params: { name: 'article', id: '42' } })
  })

  it('New button navigates to create when canWrite', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const wrapper = mountView()
    await flushPromises()
    const vm: any = wrapper.vm
    vm.onNew()
    expect(pushMock).toHaveBeenCalledWith({ name: 'collection-create', params: { name: 'article' } })
  })

  it('shows the trash switch only for a soft-delete collection with delete permission', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mountView()
    await flushPromises()
    expect((w.vm as any).showTrashSwitch).toBe(true)
  })

  it('hides the trash switch when the collection is not soft-deletable', async () => {
    seedSchema(); seedLanguage() // seedSchema's article has no softDelete
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mountView()
    await flushPromises()
    expect((w.vm as any).showTrashSwitch).toBe(false)
  })

  it('hides the trash switch without delete permission', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: false,
      permissions: { article: { read: true, write: false, delete: false } } }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mountView()
    await flushPromises()
    expect((w.vm as any).showTrashSwitch).toBe(false)
  })

  it('setMode(trash) resets page and reloads with deleted=only', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mountView()
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    ;(w.vm as any).setMode('trash')
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article',
      { page: 0, rows: 25, sort: undefined, filter: undefined, locale: 'en', deleted: 'only' })
  })

  it('edit button renders only in active mode; trash rows have no edit action', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '42' }], total: 1 })
    const w = mountView()
    await flushPromises()
    expect(w.find('[data-testid="row-edit"]').exists()).toBe(true)
    ;(w.vm as any).setMode('trash')
    await flushPromises()
    expect(w.find('[data-testid="row-edit"]').exists()).toBe(false)
  })

  it('active delete on a soft-delete collection soft-deletes (no purge) and reloads', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1' }], total: 1 })
    vi.mocked(itemsApi.remove).mockResolvedValue(undefined)
    const w = mountView()
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    await (w.vm as any).onDelete({ id: '1' })
    await flushPromises()
    expect(confirmRequire.mock.calls[0][0].message).toContain('restore')
    expect(itemsApi.remove).toHaveBeenCalledWith('article', '1')
    expect(itemsApi.list).toHaveBeenCalled() // reloaded
  })

  it('active delete on a non-soft collection uses the irreversible confirm', async () => {
    seedSchema(); seedLanguage() // no softDelete
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1', status: 'draft' }], total: 1 })
    const w = mountView()
    await flushPromises()
    await (w.vm as any).onDelete({ id: '1' })
    await flushPromises()
    expect(confirmRequire.mock.calls[0][0].message).toContain('cannot be undone')
  })

  it('purge asks for a strong confirm then removes with purge=true', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1' }], total: 1 })
    vi.mocked(itemsApi.remove).mockResolvedValue(undefined)
    const w = mountView()
    await flushPromises()
    ;(w.vm as any).setMode('trash')
    await flushPromises()
    await (w.vm as any).onPurge({ id: '1' })
    await flushPromises()
    expect(confirmRequire.mock.calls[0][0].message).toContain('Permanently')
    expect(itemsApi.remove).toHaveBeenCalledWith('article', '1', { purge: true })
  })

  it('cancelling the delete confirm skips the delete entirely', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1' }], total: 1 })
    confirmRequire.mockResolvedValue(false)
    const w = mountView()
    await flushPromises()
    await (w.vm as any).onDelete({ id: '1' })
    await flushPromises()
    expect(itemsApi.remove).not.toHaveBeenCalled()
  })

  it('cancelling the purge confirm skips the purge entirely', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1' }], total: 1 })
    confirmRequire.mockResolvedValue(false)
    const w = mountView()
    await flushPromises()
    ;(w.vm as any).setMode('trash')
    await flushPromises()
    await (w.vm as any).onPurge({ id: '1' })
    await flushPromises()
    expect(itemsApi.remove).not.toHaveBeenCalled()
  })

  it('restore calls the API directly (no confirm) and reloads', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1' }], total: 1 })
    vi.mocked(itemsApi.restore).mockResolvedValue({ id: '1' })
    const w = mountView()
    await flushPromises()
    ;(w.vm as any).setMode('trash')
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    await (w.vm as any).onRestore({ id: '1' })
    await flushPromises()
    expect(confirmRequire).not.toHaveBeenCalled()
    expect(itemsApi.restore).toHaveBeenCalledWith('article', '1')
    expect(itemsApi.list).toHaveBeenCalled()
  })

  it('deleting a row in the Language collection reloads the language store', async () => {
    mockRoute.params.name = LANGUAGE_COLLECTION
    const schema = useSchemaStore()
    schema.collections = [{
      name: LANGUAGE_COLLECTION, label: 'Language', defaultDisplayField: 'code',
      fields: [{ name: 'code', label: 'Code', interface: 'text', required: true, searchable: false,
        sortable: false, readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false }],
      relations: [],
    }]
    schema.loaded = true
    const lang = seedLanguage()
    const reloadSpy = vi.spyOn(lang, 'reload').mockResolvedValue(undefined)
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1', code: 'en' }], total: 1 })
    vi.mocked(itemsApi.remove).mockResolvedValue(undefined)
    const w = mountView()
    await flushPromises()
    await (w.vm as any).onDelete({ id: '1' })
    await flushPromises()
    expect(reloadSpy).toHaveBeenCalled()
  })

  it('deleting a row in a non-Language collection does not reload the language store', async () => {
    seedSchema() // article, no softDelete
    const lang = seedLanguage()
    const reloadSpy = vi.spyOn(lang, 'reload').mockResolvedValue(undefined)
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1', status: 'draft' }], total: 1 })
    vi.mocked(itemsApi.remove).mockResolvedValue(undefined)
    const w = mountView()
    await flushPromises()
    await (w.vm as any).onDelete({ id: '1' })
    await flushPromises()
    expect(reloadSpy).not.toHaveBeenCalled()
  })

  it('latest response wins: a slow earlier list load does not clobber a newer one', async () => {
    seedSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ status: 'initial' }], total: 1 })
    const w = mountView()
    await flushPromises()
    // Two in-flight loads. The later one (B) resolves first; the earlier (A) resolves last.
    let resolveA!: (v: unknown) => void
    let resolveB!: (v: unknown) => void
    const pA = new Promise((r) => { resolveA = r })
    const pB = new Promise((r) => { resolveB = r })
    vi.mocked(itemsApi.list)
      .mockReturnValueOnce(pA as ReturnType<typeof itemsApi.list>)
      .mockReturnValueOnce(pB as ReturnType<typeof itemsApi.list>)
    const vm = w.vm as any
    vm.loadItems() // A (older token)
    vm.loadItems() // B (newer token)
    await flushPromises() // both reach the awaited list() call
    resolveB({ data: [{ status: 'newer' }], total: 1 })
    await flushPromises()
    resolveA({ data: [{ status: 'older' }], total: 1 })
    await flushPromises()
    expect(vm.rows).toEqual([{ status: 'newer' }])
    expect(vm.loading).toBe(false)
  })

  it('surfaces an inline error when a row action fails', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1' }], total: 1 })
    vi.mocked(itemsApi.restore).mockRejectedValue(new Error('Restore failed.'))
    const w = mountView()
    await flushPromises()
    ;(w.vm as any).setMode('trash')
    await flushPromises()
    await (w.vm as any).onRestore({ id: '1' })
    await flushPromises()
    expect((w.vm as any).error).toContain('Restore failed.')
  })

  // AppShell keys <router-view> on route.path (see AppShell.test.ts's remount-contract test), so
  // switching collections (collections/:name) never reuses this instance — it unmounts the old
  // one and mounts a fresh one for the new route.params.name. There is no live params watcher to
  // "reset" here; the reset a switch produces in production IS a brand-new instance's own
  // startup state. This test asserts exactly that startup contract: a fresh mount for a given
  // collection begins with default sort/filters/mode and issues exactly one load for it.
  it('a fresh mount for a collection starts with default sort/filters/mode and loads once', async () => {
    const article = {
      name: 'article', label: 'Article', defaultDisplayField: 'status',
      fields: [{ name: 'status', label: 'Status', interface: 'select', required: false, searchable: false,
        sortable: true, readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
        options: [{ value: 'draft', label: 'Draft' }] }],
      relations: [],
    }
    const schema = useSchemaStore()
    schema.collections = [article, { ...article, name: 'other', label: 'Other' }]
    schema.loaded = true
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })

    // Simulate the remount: this IS what production does on a collection switch -- AppShell
    // tears down the 'article' instance and mounts a brand-new one keyed on the new path, which
    // reads 'other' as route.params.name from its very first render.
    mockRoute.params.name = 'other'
    const w = mountView()
    await flushPromises()
    const vm = w.vm as any

    expect(vm.tableState).toEqual({ sort: [], page: 0, pageSize: 25 })
    expect(vm.filters).toEqual({})
    expect(vm.mode).toBe('active')
    expect(itemsApi.list).toHaveBeenCalledTimes(1)
    expect(itemsApi.list).toHaveBeenCalledWith('other',
      { page: 0, rows: 25, sort: undefined, filter: undefined, locale: 'en', deleted: undefined })
  })

  it('bail (no longer readable) clears the spinner it would otherwise orphan', async () => {
    seedSchema(); seedLanguage()
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mountView()
    await flushPromises()
    const vm = w.vm as any
    // Load A goes in-flight and turns the spinner on.
    let resolveA!: (v: unknown) => void
    const pA = new Promise((r) => { resolveA = r })
    vi.mocked(itemsApi.list).mockReturnValueOnce(pA as ReturnType<typeof itemsApi.list>)
    vm.loadItems() // A: token bumped, canRead true -> loading = true, awaits pA
    await flushPromises()
    expect(vm.loading).toBe(true)
    // Load B bumps the token then bails (no longer readable). Without the fix,
    // B returns without clearing loading, and A (now stale) skips its finally -> spinner stuck.
    auth.user = { id: 'u1', isSuperAdmin: false, permissions: {} }
    vm.loadItems() // B: bails at the early return
    await flushPromises()
    expect(vm.loading).toBe(false)
    // A resolving late must not resurrect the spinner or pollute rows.
    resolveA({ data: [{ status: 'stale' }], total: 1 })
    await flushPromises()
    expect(vm.loading).toBe(false)
    expect(vm.rows).toEqual([])
  })

  it('renders the PageHeader title and count caption', async () => {
    seedSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ status: 'draft' }], total: 1 })
    const w = mountView()
    await flushPromises()
    expect(w.get('h1').text()).toBe('Article')
    expect(w.text()).toContain('1 items') // collectionList.count with n=total
  })

  it('trash mode shows notice banner and deletedAt column', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({
      data: [{ id: '42', deletedAt: '2026-07-23T10:00:00Z', translations: {} }], total: 1,
    })
    const w = mountView()
    await flushPromises()
    expect(w.find('[role="status"]').exists()).toBe(false)
    ;(w.vm as any).setMode('trash')
    await flushPromises()
    expect(w.find('[role="status"]').exists()).toBe(true)
    expect(w.text()).toContain('2026')
  })

  // The columns/isSelectField helpers are still live
  // (they drive the Badge branch).
  it('orders the default display field first and detects select-type columns', async () => {
    seedSchema(); seedLanguage() // seedSchema's article has defaultDisplayField: 'status' (interface: select)
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ status: 'draft' }], total: 1 })
    const w = mountView()
    await flushPromises()
    const vm = w.vm as any
    expect(vm.columns[0].field).toBe('status')
    expect(vm.isSelectField('status')).toBe(true)
    expect(vm.isSelectField('title')).toBe(false)
  })

  // Searchable is what the backend's `search=` honours, and chapter 4 tells collection authors it
  // governs the collection's free-text search — but selectListColumns caps at 6 columns and only
  // admits list-displayable interfaces, so a searchable RichText body became unreachable from the
  // list once FilterBuilder replaced the old free-text ListToolbar. It is offered here instead.
  it('offers searchable fields that did not make the display-column cut to FilterBuilder', async () => {
    const article = {
      name: 'article',
      label: 'Article',
      defaultDisplayField: 'status',
      fields: [
        { name: 'status', label: 'Status', interface: 'select', required: false, searchable: false,
          sortable: true, readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
          options: [{ value: 'draft', label: 'Draft' }] },
        // richText has listColumn === null, so selectListColumns excludes it: display-ineligible
        // but searchable, i.e. exactly the field class this test exists for.
        { name: 'body', label: 'Body', interface: 'richText', required: false, searchable: true,
          sortable: false, readOnly: false, hidden: false, translatable: true, sort: 1, isSystem: false },
        // Hidden fields must never be offered: QueryValidator excludes them from its allowlist
        // deliberately (they hold credentials), so filtering one is a guaranteed 400.
        { name: 'internalSlug', label: 'Internal Slug', interface: 'text', required: false,
          searchable: true, sortable: false, readOnly: false, hidden: true, translatable: false,
          sort: 2, isSystem: false },
      ],
      relations: [],
    }
    const schema = useSchemaStore()
    schema.collections = [article]
    schema.loaded = true
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })

    const w = mountView()
    await flushPromises()

    const names = (w.findComponent(FilterBuilder).props('fields') as { name: string }[]).map((f) => f.name)
    expect(names).toContain('status')
    expect(names).toContain('body')
    expect(names).not.toContain('internalSlug')
    // The display columns still come first, in their existing order.
    expect(names[0]).toBe('status')
  })

  // A select field cut by the 6-column display limit still needs its choices carried into
  // FilterBuilder, or the user gets a free-text Input and types the option LABEL while the
  // backend only matches the stored value -- a filter that silently returns zero rows.
  it('carries options for a searchable select field that missed the display-column cut', async () => {
    const filler = (n: number) => ({
      name: `filler${n}`, label: `Filler ${n}`, interface: 'text', required: false, searchable: false,
      sortable: false, readOnly: false, hidden: false, translatable: false, sort: n, isSystem: false,
    })
    const article = {
      name: 'article',
      label: 'Article',
      defaultDisplayField: null,
      fields: [
        ...Array.from({ length: 6 }, (_, i) => filler(i)),
        { name: 'status', label: 'Status', interface: 'select', required: false, searchable: true,
          sortable: false, readOnly: false, hidden: false, translatable: false, sort: 6, isSystem: false,
          options: [{ value: 'draft', label: 'Draft' }, { value: 'published', label: 'Published' }] },
      ],
      relations: [],
    }
    const schema = useSchemaStore()
    schema.collections = [article]
    schema.loaded = true
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })

    const w = mountView()
    await flushPromises()

    const fields = w.findComponent(FilterBuilder).props('fields') as { name: string; options?: unknown }[]
    const status = fields.find((f) => f.name === 'status')
    expect(status).toBeTruthy()
    expect(status!.options).toEqual([{ value: 'draft', label: 'Draft' }, { value: 'published', label: 'Published' }])
  })
})
