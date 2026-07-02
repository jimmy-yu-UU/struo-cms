import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import ItemFormView from './ItemFormView.vue'
import { itemsApi } from '../api/itemsApi'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'

const push = vi.fn()
let routeParams: Record<string, string> = {}
let routeName = 'collection-item'
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: routeParams, name: routeName }),
  useRouter: () => ({ push }),
}))
const confirmRequire = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))

const meta = { name: 'article', label: 'Article', fields: [
  { name: 'status', label: 'Status', interface: 'text', required: true, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false },
]}
const stubs = { ItemForm: true, Button: true, ConfirmDialog: true }

function setupStores(opts: { superAdmin?: boolean } = {}) {
  const auth = useAuthStore()
  auth.user = { id: '1', isSuperAdmin: opts.superAdmin ?? true, permissions: {} }
  const schema = useSchemaStore()
  schema.load = vi.fn().mockResolvedValue(undefined)
  schema.get = vi.fn().mockReturnValue(meta) as never
  const lang = useLanguageStore()
  lang.load = vi.fn().mockResolvedValue(undefined)
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  return { auth, schema, lang }
}

describe('ItemFormView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockClear()
    confirmRequire.mockClear()
    vi.restoreAllMocks()
    routeParams = {}
    routeName = 'collection-item'
  })

  it('edit path loads the item and inflates the model', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const spy = vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'published', translations: {} })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    expect(spy).toHaveBeenCalledWith('article', '5')
    expect((w.vm as any).model.shared.status).toBe('published')
  })

  it('create path builds a blank model and calls create on submit', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    setupStores()
    const spy = vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: '9' })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'draft'
    await (w.vm as any).onSubmit()
    expect(spy).toHaveBeenCalledWith('article', expect.objectContaining({ status: 'draft' }))
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
  })

  it('blocks submit and shows errors when required field empty', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    setupStores()
    const spy = vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: '9' })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    await (w.vm as any).onSubmit()
    expect(spy).not.toHaveBeenCalled()
    expect((w.vm as any).errors.status).toMatch(/required/i)
  })

  it('marks notFound when the item is missing', async () => {
    routeParams = { name: 'article', id: '404' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockRejectedValue(new Error('Item not found.'))
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    expect((w.vm as any).notFound).toBe(true)
  })

  it('delete requires confirmation then removes and routes back', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const rm = vi.spyOn(itemsApi, 'remove').mockResolvedValue(undefined)
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).onDelete()
    expect(confirmRequire).toHaveBeenCalled()
    // invoke the accept callback the component passed to confirm.require
    await confirmRequire.mock.calls[0][0].accept()
    expect(rm).toHaveBeenCalledWith('article', '5')
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
  })
})
