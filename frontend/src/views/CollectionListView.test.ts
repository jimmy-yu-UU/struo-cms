import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { reactive, computed, provide, inject } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import CollectionListView from './CollectionListView.vue'
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
// The DataTable/Column stubs render just enough real markup for the row-click and
// icon-presence assertions below: a single <tbody><tr> to click on, and each Column's
// #body scoped slot rendered once so action-button icons appear in the DOM. Neither
// stub replicates PrimeVue's real per-row Column dispatch (only the first `value` row
// is ever rendered) -- a reactive `computed` provide/inject plumbs that single row's
// real data down to each Column so cell-content assertions (e.g. the deletedAt column)
// can see it, and stay current when `value` changes after an async reload.
vi.mock('primevue/datatable', () => ({
  default: {
    name: 'DataTable',
    props: ['value'],
    setup(props: { value: Record<string, unknown>[] }) {
      provide('rowData', computed(() => props.value?.[0] ?? {}))
      return {}
    },
    template: '<table><tbody><tr><slot /></tr></tbody></table>',
  },
}))
vi.mock('primevue/column', () => ({
  default: {
    name: 'Column',
    setup() {
      return { rowData: inject('rowData', computed(() => ({}))) }
    },
    template: '<div><slot name="body" :data="rowData" /></div>',
  },
}))
vi.mock('primevue/inputtext', () => ({ default: { name: 'InputText', template: '<input />' } }))
vi.mock('primevue/selectbutton', () => ({ default: { name: 'SelectButton', template: '<div />' } }))
vi.mock('primevue/confirmdialog', () => ({ default: { name: 'ConfirmDialog', template: '<div />' } }))
vi.mock('primevue/button', () => ({ default: { name: 'Button', template: '<button />' } }))
const confirmRequire = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))

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
    setActivePinia(createPinia()); vi.clearAllMocks(); pushMock.mockClear(); confirmRequire.mockClear()
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
    expect(itemsApi.list).toHaveBeenCalledWith('article', { page: 0, rows: 25, sort: undefined, search: undefined, locale: 'en' })
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
    expect(itemsApi.list).toHaveBeenCalledWith('article', { page: 0, rows: 25, sort: undefined, search: undefined, locale: 'en' })
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

  it('onSort builds a descending token and reloads', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const wrapper = mountView()
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    ;(wrapper.vm as unknown as { onSort: (e: unknown) => void }).onSort({ sortField: 'status', sortOrder: -1 })
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article', { page: 0, rows: 25, sort: '-status', search: undefined, locale: 'en' })
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

  it('read-only viewer sees a view (eye) action instead of edit, and onEdit still navigates', async () => {
    seedSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: false,
      permissions: { article: { read: true, write: false, delete: false } } }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '42', status: 'draft' }], total: 1 })
    const w = mountView()
    await flushPromises()
    expect(w.html()).toContain('pi-eye')
    expect(w.html()).not.toContain('pi-pencil')
    const vm: any = w.vm
    vm.onEdit({ id: '42' })
    expect(pushMock).toHaveBeenCalledWith({ name: 'collection-item', params: { name: 'article', id: '42' } })
  })

  it('clicking a row body does not navigate', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '42', translations: {} }], total: 1 })
    const wrapper = mountView()
    await flushPromises()
    // Emit the DataTable's own row-click component event (not a native DOM click on the
    // stub's <tr>, which the stub never translates into anything) -- this is what a real
    // PrimeVue DataTable emits on row click. If a listener were ever bound back onto
    // DataTable (e.g. @row-click="onEdit"), Vue attribute fallthrough would deliver it as
    // an onRowClick prop on the stub, and $emit('row-click', ...) below would invoke it,
    // driving pushMock -- so this assertion actually fails against that regression.
    wrapper.findComponent({ name: 'DataTable' }).vm.$emit('row-click', { data: { id: '42' } })
    await flushPromises()
    expect(pushMock).not.toHaveBeenCalled()
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
      { page: 0, rows: 25, sort: undefined, search: undefined, locale: 'en', deleted: 'only' })
  })

  it('edit button renders only in active mode; trash rows have no edit action', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '42' }], total: 1 })
    const w = mountView()
    await flushPromises()
    expect(w.html()).toContain('pi-pencil')
    ;(w.vm as any).setMode('trash')
    await flushPromises()
    expect(w.html()).not.toContain('pi-pencil')
  })

  it('active delete on a soft-delete collection soft-deletes (no purge) and reloads', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1' }], total: 1 })
    vi.mocked(itemsApi.remove).mockResolvedValue(undefined)
    const w = mountView()
    await flushPromises()
    ;(w.vm as any).onDelete({ id: '1' })
    const arg = confirmRequire.mock.calls[0][0]
    expect(arg.message).toContain('restore')
    vi.mocked(itemsApi.list).mockClear()
    await arg.accept()
    await flushPromises()
    expect(itemsApi.remove).toHaveBeenCalledWith('article', '1')
    expect(itemsApi.list).toHaveBeenCalled() // reloaded
  })

  it('active delete on a non-soft collection uses the irreversible confirm', async () => {
    seedSchema(); seedLanguage() // no softDelete
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1', status: 'draft' }], total: 1 })
    const w = mountView()
    await flushPromises()
    ;(w.vm as any).onDelete({ id: '1' })
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
    ;(w.vm as any).onPurge({ id: '1' })
    const arg = confirmRequire.mock.calls[0][0]
    expect(arg.message).toContain('Permanently')
    await arg.accept()
    expect(itemsApi.remove).toHaveBeenCalledWith('article', '1', { purge: true })
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
    ;(w.vm as any).onDelete({ id: '1' })
    const arg = confirmRequire.mock.calls[0][0]
    await arg.accept()
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
    ;(w.vm as any).onDelete({ id: '1' })
    const arg = confirmRequire.mock.calls[0][0]
    await arg.accept()
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

  // Search debounce hygiene (unmount/switch cancel) + bail clears orphaned spinner.
  // Fake only setTimeout/clearTimeout so flushPromises (setImmediate-based) still resolves promises.
  it('coalesces rapid search input into a single load after 300ms (debounce merge)', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    seedSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mountView()
    await flushPromises() // initial onMounted load
    vi.mocked(itemsApi.list).mockClear()
    const vm = w.vm as any
    vm.onSearchInput('a')
    vm.onSearchInput('ab')
    vm.onSearchInput('abc')
    vi.advanceTimersByTime(299)
    await flushPromises()
    expect(itemsApi.list).not.toHaveBeenCalled() // still within the debounce window
    vi.advanceTimersByTime(1)
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledTimes(1)
    expect(itemsApi.list).toHaveBeenCalledWith('article',
      { page: 0, rows: 25, sort: undefined, search: 'abc', locale: 'en' })
  })

  it('unmount cancels a pending search: no list load fires after the component is gone', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    seedSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mountView()
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    ;(w.vm as any).onSearchInput('foo') // schedules a debounced load
    w.unmount()
    vi.advanceTimersByTime(300)
    await flushPromises()
    expect(itemsApi.list).not.toHaveBeenCalled()
  })

  it('switching collection cancels the pending search: only watch(name) reloads (not the typed search)', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
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
    const w = mountView()
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    ;(w.vm as any).onSearchInput('foo') // pending debounced search against 'article'
    mockRoute.params.name = 'other' // switch collection -> watch(name) must cancel the pending search
    await flushPromises()
    // watch(name) fired exactly one reload for the new collection, with search reset.
    expect(itemsApi.list).toHaveBeenCalledTimes(1)
    expect(itemsApi.list).toHaveBeenCalledWith('other',
      { page: 0, rows: 25, sort: undefined, search: undefined, locale: 'en' })
    // The cancelled search must never fire, even after its window elapses.
    vi.advanceTimersByTime(300)
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledTimes(1)
    expect(itemsApi.list).not.toHaveBeenCalledWith('article',
      { page: 0, rows: 25, sort: '-status', search: 'foo', locale: 'en' })
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
    expect(w.find('.trash-banner').exists()).toBe(false)
    ;(w.vm as any).setMode('trash')
    await flushPromises()
    expect(w.find('.trash-banner').exists()).toBe(true)
    expect(w.text()).toContain('2026')
  })

  // The columns/isSelectField helpers are still live
  // (they drive the Tag branch) even though linkField/row-link coverage was removed.
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
})
