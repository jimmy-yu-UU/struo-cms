import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import ItemFormView from './ItemFormView.vue'
import { itemsApi } from '../api/itemsApi'
import { ApiError } from '../api/apiClient'
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
], relations: [
  { name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false },
  { name: 'comments', label: 'Comments', kind: 'oneToMany', targetCollection: 'comment', interface: 'relatedList', foreignKey: 'ArticleId', displayTemplate: '{Body}', editable: false, selfReferencing: false },
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
    expect(spy).toHaveBeenCalledWith('article', '5', expect.objectContaining({ deep: ['category'], locale: 'en' }))
    expect((w.vm as any).model.shared.status).toBe('published')
  })

  it('fetches with deep = editable relation names on edit', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const spy = vi.spyOn(itemsApi, 'get').mockResolvedValue({
      id: '5', status: 'published', translations: {}, category: { id: 'cat-1' },
    })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    expect(spy).toHaveBeenCalledWith('article', '5', expect.objectContaining({ deep: ['category'] }))
  })

  it('inflates relation current values into the model on edit', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({
      id: '5', status: 'published', translations: {}, category: { id: 'cat-1' },
    })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    expect((w.vm as any).model.relations.category).toBe('cat-1')
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

  it('maps server validation error.details onto per-field errors', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    setupStores()
    const spy = vi.spyOn(itemsApi, 'create').mockRejectedValue(
      new ApiError(400, 'One or more validation errors occurred.', 'VALIDATION', [
        { field: 'status', message: 'Status is already taken.' },
      ]),
    )
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'draft' // pass client validation
    await (w.vm as any).onSubmit()
    expect(spy).toHaveBeenCalled()
    expect((w.vm as any).errors.status).toBe('Status is already taken.')
    expect((w.vm as any).serverError).toBe('')
  })

  it('splits mixed details: matched field to errors, unknown to serverError banner', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    setupStores()
    vi.spyOn(itemsApi, 'create').mockRejectedValue(
      new ApiError(400, 'One or more validation errors occurred.', 'VALIDATION', [
        { field: 'status', message: 'Status is already taken.' },
        { field: 'mystery', message: 'Server rejected a hidden field.' },
      ]),
    )
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'draft'
    await (w.vm as any).onSubmit()
    expect((w.vm as any).errors.status).toBe('Status is already taken.')
    expect((w.vm as any).serverError).toContain('Server rejected a hidden field.')
  })

  it('falls back to serverError banner when the error has no details', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    setupStores()
    vi.spyOn(itemsApi, 'create').mockRejectedValue(
      new ApiError(400, 'Title is required.', 'BAD_USER_INPUT'),
    )
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'draft'
    await (w.vm as any).onSubmit()
    expect((w.vm as any).serverError).toBe('Title is required.')
    expect(Object.keys((w.vm as any).errors)).toHaveLength(0)
  })

  it('marks notFound when the server returns code NOT_FOUND', async () => {
    routeParams = { name: 'article', id: '404' }
    setupStores()
    // CJK message that does NOT match the old /not found/i regex — only the code branch can pass this.
    vi.spyOn(itemsApi, 'get').mockRejectedValue(new ApiError(404, '找不到資源', 'NOT_FOUND'))
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    expect((w.vm as any).notFound).toBe(true)
  })

  it('shows serverError (not notFound) on a non-404 load error', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockRejectedValue(new ApiError(500, 'Boom', 'INTERNAL_SERVER_ERROR'))
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    expect((w.vm as any).notFound).toBe(false)
    expect((w.vm as any).serverError).toBe('Boom')
  })

  it('carries the loaded version through to the update payload (FE-4 chain fix)', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 3 })
    const upd = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5' })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'edited'
    await (w.vm as any).onSubmit()
    expect(upd).toHaveBeenCalledWith('article', '5', expect.objectContaining({ version: 3 }))
  })

  it('recovers from 409 CONFLICT: refreshes version, flags conflict, preserves edits, then re-saves', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    // Default (steady-state) load returns version 1; init runs via onMounted AND the explicit call.
    const get = vi.spyOn(itemsApi, 'get')
      .mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    const upd = vi.spyOn(itemsApi, 'update')
      .mockRejectedValueOnce(new ApiError(409, 'The item was modified by someone else.', 'CONFLICT'))
      .mockResolvedValueOnce({ id: '5' })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'my-edit'
    // The next get call is the post-409 recovery refresh: return the bumped server version.
    get.mockResolvedValueOnce({ id: '5', status: 'published', translations: {}, version: 7 })
    await (w.vm as any).onSubmit() // triggers 409 -> recovery
    expect((w.vm as any).conflict).toBe(true)
    expect((w.vm as any).model.version).toBe(7)
    expect((w.vm as any).model.shared.status).toBe('my-edit') // user edits NOT overwritten
    // re-save now echoes the refreshed version and succeeds
    await (w.vm as any).onSubmit()
    expect(upd).toHaveBeenLastCalledWith('article', '5', expect.objectContaining({ version: 7 }))
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
  })

  it('Reload latest overwrites the model with the server copy and clears conflict', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const get = vi.spyOn(itemsApi, 'get')
      .mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    vi.spyOn(itemsApi, 'update').mockRejectedValueOnce(new ApiError(409, 'Conflict', 'CONFLICT'))
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'my-edit'
    get.mockResolvedValueOnce({ id: '5', status: 'server-copy', translations: {}, version: 9 })
    await (w.vm as any).onSubmit()
    expect((w.vm as any).conflict).toBe(true)
    ;(w.vm as any).reloadLatest()
    expect((w.vm as any).model.shared.status).toBe('server-copy') // full overwrite
    expect((w.vm as any).model.version).toBe(9)
    expect((w.vm as any).conflict).toBe(false)
    expect((w.vm as any).serverError).toBe('')
  })

  it('falls back to notFound when the post-409 refresh 404s (item deleted)', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const get = vi.spyOn(itemsApi, 'get')
      .mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    vi.spyOn(itemsApi, 'update').mockRejectedValueOnce(new ApiError(409, 'Conflict', 'CONFLICT'))
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'my-edit'
    get.mockRejectedValueOnce(new ApiError(404, '找不到資源', 'NOT_FOUND'))
    await (w.vm as any).onSubmit()
    expect((w.vm as any).notFound).toBe(true)
    expect((w.vm as any).conflict).toBe(false)
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

  it('delete on a soft-delete collection uses the move-to-trash confirm', async () => {
    routeParams = { name: 'article', id: '5' }
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue({ ...meta, softDelete: true })
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).onDelete()
    expect(confirmRequire.mock.calls[0][0].message).toContain('restore')
  })

  it('delete on a non-soft collection keeps the irreversible confirm', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores() // meta has no softDelete
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mount(ItemFormView, { global: { stubs } })
    await w.vm.init()
    ;(w.vm as any).onDelete()
    expect(confirmRequire.mock.calls[0][0].message).toContain('cannot be undone')
  })
})
