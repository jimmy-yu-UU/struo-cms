import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import CollectionListView from './CollectionListView.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { itemsApi } from '../api/itemsApi'

const pushMock = vi.fn()
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { name: 'article' } }),
  useRouter: () => ({ push: pushMock }),
}))
vi.mock('../api/itemsApi', () => ({ itemsApi: { list: vi.fn() } }))
vi.mock('primevue/datatable', () => ({ default: { name: 'DataTable', template: '<div><slot /></div>' } }))
vi.mock('primevue/column', () => ({ default: { name: 'Column', template: '<div />' } }))
vi.mock('primevue/inputtext', () => ({ default: { name: 'InputText', template: '<input />' } }))

function seedSchema() {
  const schema = useSchemaStore()
  schema.collections = [{
    name: 'article', label: 'Article', defaultDisplayField: 'status',
    fields: [{ name: 'status', label: 'Status', interface: 'select', required: false, searchable: false,
      sortable: true, readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
      options: [{ value: 'draft', label: 'Draft' }] }],
  }]
}

describe('CollectionListView', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks(); pushMock.mockClear() })

  it('loads items on mount for a readable collection', async () => {
    seedSchema()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ status: 'draft' }], total: 1 })
    mount(CollectionListView)
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article', { page: 0, rows: 25, sort: undefined, search: undefined })
  })

  it('shows a permission message and makes no API call when not readable', async () => {
    seedSchema()
    useAuthStore().user = { id: 'u1', isSuperAdmin: false, permissions: {} }
    const wrapper = mount(CollectionListView)
    await flushPromises()
    expect(itemsApi.list).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain("don't have access")
  })

  it('shows an error message when the list load fails', async () => {
    seedSchema()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockRejectedValue(new Error('Server error.'))
    const wrapper = mount(CollectionListView)
    await flushPromises()
    expect(wrapper.text()).toContain('Server error.')
  })

  it('onSort builds a descending token and reloads', async () => {
    seedSchema()
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const wrapper = mount(CollectionListView)
    await flushPromises()
    vi.mocked(itemsApi.list).mockClear()
    ;(wrapper.vm as unknown as { onSort: (e: unknown) => void }).onSort({ sortField: 'status', sortOrder: -1 })
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('article', { page: 0, rows: 25, sort: '-status', search: undefined })
  })

  it('navigates to the item on row click', async () => {
    seedSchema()
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
    useAuthStore().user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    const wrapper = mount(CollectionListView)
    await flushPromises()
    const vm: any = wrapper.vm
    vm.onNew()
    expect(pushMock).toHaveBeenCalledWith({ name: 'collection-create', params: { name: 'article' } })
  })
})
