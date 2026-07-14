import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import CollectionListView from './CollectionListView.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { itemsApi } from '../api/itemsApi'

const pushMock = vi.fn()
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { name: 'article' } }),
  useRouter: () => ({ push: pushMock }),
}))
vi.mock('../api/itemsApi', () => ({
  itemsApi: { list: vi.fn(), remove: vi.fn(), restore: vi.fn() },
}))
vi.mock('primevue/datatable', () => ({ default: { name: 'DataTable', template: '<div><slot /></div>' } }))
vi.mock('primevue/column', () => ({ default: { name: 'Column', template: '<div />' } }))
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
}

function seedLanguage() {
  const lang = useLanguageStore()
  lang.load = vi.fn().mockResolvedValue(undefined)
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  return lang
}

describe('CollectionListView', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks(); pushMock.mockClear(); confirmRequire.mockClear() })

  it('loads items on mount for a readable collection', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ status: 'draft' }], total: 1 })
    mount(CollectionListView)
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article', { page: 0, rows: 25, sort: undefined, search: undefined, locale: 'en' })
  })

  it('renders a translatable column from translations[locale] instead of "—"', async () => {
    const schema = useSchemaStore()
    schema.collections = [{
      name: 'article', label: 'Article', defaultDisplayField: 'title',
      fields: [{ name: 'title', label: 'Title', interface: 'text', required: false, searchable: false,
        sortable: true, readOnly: false, hidden: false, translatable: true, sort: 0, isSystem: false }],
      relations: [],
    }]
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({
      data: [{ id: '1', translations: { en: { title: 'Hello' } } }],
      total: 1,
    })
    const wrapper = mount(CollectionListView)
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
    const wrapper = mount(CollectionListView)
    await flushPromises()
    expect(itemsApi.list).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain("don't have access")
  })

  it('shows an error message when the list load fails', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockRejectedValue(new Error('Server error.'))
    const wrapper = mount(CollectionListView)
    await flushPromises()
    expect(wrapper.text()).toContain('Server error.')
  })

  it('onSort builds a descending token and reloads', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const wrapper = mount(CollectionListView)
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    ;(wrapper.vm as unknown as { onSort: (e: unknown) => void }).onSort({ sortField: 'status', sortOrder: -1 })
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article', { page: 0, rows: 25, sort: '-status', search: undefined, locale: 'en' })
  })

  it('navigates to the item on row click', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const wrapper = mount(CollectionListView)
    await flushPromises()
    const vm: any = wrapper.vm
    vm.onRowClick({ data: { id: '42' } })
    expect(pushMock).toHaveBeenCalledWith({ name: 'collection-item', params: { name: 'article', id: '42' } })
  })

  it('New button navigates to create when canWrite', async () => {
    seedSchema()
    seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const wrapper = mount(CollectionListView)
    await flushPromises()
    const vm: any = wrapper.vm
    vm.onNew()
    expect(pushMock).toHaveBeenCalledWith({ name: 'collection-create', params: { name: 'article' } })
  })

  it('shows the trash switch only for a soft-delete collection with delete permission', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mount(CollectionListView)
    await flushPromises()
    expect((w.vm as any).showTrashSwitch).toBe(true)
  })

  it('hides the trash switch when the collection is not soft-deletable', async () => {
    seedSchema(); seedLanguage() // seedSchema's article has no softDelete
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mount(CollectionListView)
    await flushPromises()
    expect((w.vm as any).showTrashSwitch).toBe(false)
  })

  it('hides the trash switch without delete permission', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: false,
      permissions: { article: { read: true, write: false, delete: false } } }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mount(CollectionListView)
    await flushPromises()
    expect((w.vm as any).showTrashSwitch).toBe(false)
  })

  it('setMode(trash) resets page and reloads with deleted=only', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mount(CollectionListView)
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    ;(w.vm as any).setMode('trash')
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article',
      { page: 0, rows: 25, sort: undefined, search: undefined, locale: 'en', deleted: 'only' })
  })

  it('does not navigate on row click in trash mode', async () => {
    seedSoftSchema(); seedLanguage()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const w = mount(CollectionListView)
    await flushPromises()
    ;(w.vm as any).setMode('trash')
    ;(w.vm as any).onRowClick({ data: { id: '42' } })
    expect(pushMock).not.toHaveBeenCalled()
  })
})
