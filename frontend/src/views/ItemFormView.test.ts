import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import ItemFormView from './ItemFormView.vue'
import PermissionMatrix from '../components/rbac/PermissionMatrix.vue'
import EffectivePermissionsPanel from '../components/rbac/EffectivePermissionsPanel.vue'
import { itemsApi } from '../api/itemsApi'
import { ApiError } from '../api/apiClient'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { languagesApi } from '../api/languagesApi'
import { rbacApi } from '../api/rbacApi'

// Batch B Task 8: PermissionMatrix / EffectivePermissionsPanel are mounted for real (not stubbed)
// so the reload/existence assertions below exercise the actual components; stub only their API.
vi.mock('../api/rbacApi', () => ({
  rbacApi: {
    getRolePermissions: vi.fn(),
    putRolePermissions: vi.fn(),
    getEffectivePermissions: vi.fn(),
  },
}))

const push = vi.fn()
let routeParams: Record<string, string> = {}
let routeName = 'collection-item'
// Capture the guards registered via onBeforeRouteLeave / onBeforeRouteUpdate so tests can invoke
// them directly.
type RouteLoc = { params: Record<string, string | undefined> }
let leaveGuard: (() => Promise<boolean> | boolean) | null = null
let updateGuard: ((to: RouteLoc, from: RouteLoc) => Promise<boolean> | boolean) | null = null
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: routeParams, name: routeName }),
  useRouter: () => ({ push }),
  onBeforeRouteLeave: (guard: () => Promise<boolean> | boolean) => { leaveGuard = guard },
  onBeforeRouteUpdate: (guard: (to: RouteLoc, from: RouteLoc) => Promise<boolean> | boolean) => { updateGuard = guard },
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
const stubs = { ItemForm: true, Button: true, ConfirmDialog: true, RevisionHistoryDrawer: true }

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { itemForm: {
    loading: 'Loading…', collectionNotFound: 'Collection not found', itemNotFound: 'Item not found',
    noCreatePermission: "You don't have permission to create items here",
    new: 'New {label}', edit: 'Edit {label}', delete: 'Delete', save: 'Save', back: 'Back to list',
    relations: 'Relations', translatableBadge: 'Translatable',
    conflictText: 'This item was changed by someone else.', reloadLatest: 'Reload latest',
  },
    revisions: { open: 'History', title: 'Revision history', reverted: 'Reverted to {n}' },
    confirm: {
      unsavedHeader: 'Unsaved changes',
      unsavedMessage: 'You have unsaved changes. Leave this page and discard them?',
      softDeleteHeader: 'Move to trash',
      softDeleteMessage: 'Move this item to trash? You can restore it later.',
      hardDeleteHeader: 'Confirm delete',
      hardDeleteMessage: 'Delete this item? This cannot be undone.',
    },
    rbac: {
      matrixTitle: 'Permissions',
      colCollection: 'Collection',
      colRead: 'Read',
      colWrite: 'Write',
      colDelete: 'Delete',
      save: 'Save permissions',
      saved: 'Permissions saved',
      loadFailed: 'Failed to load permissions',
      saveFailed: 'Failed to save permissions',
      superAdminAll: 'This role is a super admin and has full access to everything.',
      adminOnlyWriteHint: 'Writes to this collection always require a super admin',
      effectiveTitle: 'Effective permissions',
      effectiveSuperAdmin: 'This user is a super admin and has full access to everything.',
      effectiveEmpty: 'No permissions',
      effectiveLoadFailed: 'Failed to load effective permissions',
    },
  } },
})
function mountView() {
  return mount(ItemFormView, {
    global: { plugins: [i18n, PrimeVue, ToastService, ConfirmationService], stubs },
  })
}

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
    leaveGuard = null
    updateGuard = null
  })

  it('edit path loads the item and inflates the model', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const spy = vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'published', translations: {} })
    const w = mountView()
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
    const w = mountView()
    await w.vm.init()
    expect(spy).toHaveBeenCalledWith('article', '5', expect.objectContaining({ deep: ['category'] }))
  })

  it('inflates relation current values into the model on edit', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({
      id: '5', status: 'published', translations: {}, category: { id: 'cat-1' },
    })
    const w = mountView()
    await w.vm.init()
    expect((w.vm as any).model.relations.category).toBe('cat-1')
  })

  it('create path builds a blank model and calls create on submit', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    setupStores()
    const spy = vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: '9' })
    const w = mountView()
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
    const w = mountView()
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
    const w = mountView()
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
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'draft'
    await (w.vm as any).onSubmit()
    expect((w.vm as any).errors.status).toBe('Status is already taken.')
    expect((w.vm as any).serverError).toContain('Server rejected a hidden field.')
  })

  it('joins multiple leftover (unknown-field) messages with "; " in the banner', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    setupStores()
    vi.spyOn(itemsApi, 'create').mockRejectedValue(
      new ApiError(400, 'One or more validation errors occurred.', 'VALIDATION', [
        { field: 'ghost1', message: 'First hidden problem.' },
        { field: 'ghost2', message: 'Second hidden problem.' },
      ]),
    )
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'draft'
    await (w.vm as any).onSubmit()
    expect((w.vm as any).serverError).toBe('First hidden problem.; Second hidden problem.')
  })

  it('falls back to serverError banner when the error has no details', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    setupStores()
    vi.spyOn(itemsApi, 'create').mockRejectedValue(
      new ApiError(400, 'Title is required.', 'BAD_USER_INPUT'),
    )
    const w = mountView()
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
    const w = mountView()
    await w.vm.init()
    expect((w.vm as any).notFound).toBe(true)
  })

  it('shows serverError (not notFound) on a non-404 load error', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockRejectedValue(new ApiError(500, 'Boom', 'INTERNAL_SERVER_ERROR'))
    const w = mountView()
    await w.vm.init()
    expect((w.vm as any).notFound).toBe(false)
    expect((w.vm as any).serverError).toBe('Boom')
  })

  it('carries the loaded version through to the update payload (FE-4 chain fix)', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 3 })
    const upd = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5' })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'edited'
    await (w.vm as any).onSubmit()
    expect(upd).toHaveBeenCalledWith('article', '5', expect.objectContaining({ version: 3 }))
  })

  it('recovers from 409 VERSION_CONFLICT: refreshes version, flags conflict, preserves edits, then re-saves', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    // Default (steady-state) load returns version 1; init runs via onMounted AND the explicit call.
    const get = vi.spyOn(itemsApi, 'get')
      .mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    const upd = vi.spyOn(itemsApi, 'update')
      .mockRejectedValueOnce(new ApiError(409, 'The item was modified by someone else.', 'VERSION_CONFLICT'))
      .mockResolvedValueOnce({ id: '5' })
    const w = mountView()
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

  it('generic 409 CONFLICT (e.g. duplicate email) shows the banner and does NOT trigger conflict recovery (API-1)', async () => {
    // API-1: only optimistic-lock clashes carry code VERSION_CONFLICT. Other 409s (delete-restrict,
    // duplicate email) keep the generic CONFLICT code and must NOT arm the "changed by someone else"
    // recovery banner — they fall through to the plain serverError banner with no version refetch.
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const get = vi.spyOn(itemsApi, 'get')
      .mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    vi.spyOn(itemsApi, 'update')
      .mockRejectedValueOnce(new ApiError(409, 'Email is already in use.', 'CONFLICT'))
    const w = mountView()
    await w.vm.init() // init runs via onMounted AND this explicit call, so get is already called
    const getCallsAfterLoad = get.mock.calls.length
    ;(w.vm as any).model.shared.status = 'my-edit'
    await (w.vm as any).onSubmit()
    // No conflict recovery: banner not armed, edits not touched, and NO recovery refetch (get unchanged).
    expect((w.vm as any).conflict).toBe(false)
    expect((w.vm as any).serverError).toBe('Email is already in use.')
    expect(get.mock.calls.length).toBe(getCallsAfterLoad)
  })

  it('Reload latest overwrites the model with the server copy and clears conflict', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const get = vi.spyOn(itemsApi, 'get')
      .mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    vi.spyOn(itemsApi, 'update').mockRejectedValueOnce(new ApiError(409, 'Conflict', 'VERSION_CONFLICT'))
    const w = mountView()
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

  it('init clears stale 409-recovery state (conflict + cached server copy) on re-invocation', async () => {
    // Fix 1: a re-entrant init (e.g. route param change) must not carry item A's conflict banner
    // or cached "reload latest" copy into item B.
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const get = vi.spyOn(itemsApi, 'get')
      .mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    vi.spyOn(itemsApi, 'update').mockRejectedValueOnce(new ApiError(409, 'Conflict', 'VERSION_CONFLICT'))
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'my-edit'
    // Post-409 refresh caches item A's server copy in latestFromServer.
    get.mockResolvedValueOnce({ id: '5', status: 'item-A-server', translations: {}, version: 9 })
    await (w.vm as any).onSubmit()
    expect((w.vm as any).conflict).toBe(true)

    // Now init runs again for item B. Steady-state get() returns item B.
    get.mockResolvedValue({ id: '6', status: 'item-B', translations: {}, version: 1 })
    routeParams = { name: 'article', id: '6' }
    await (w.vm as any).init()
    expect((w.vm as any).conflict).toBe(false)
    expect((w.vm as any).model.shared.status).toBe('item-B')

    // The stale cached copy must be gone: reloadLatest is now a no-op, not a write of item A.
    ;(w.vm as any).reloadLatest()
    expect((w.vm as any).model.shared.status).toBe('item-B')
  })

  it('falls back to notFound when the post-409 refresh 404s (item deleted)', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const get = vi.spyOn(itemsApi, 'get')
      .mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    vi.spyOn(itemsApi, 'update').mockRejectedValueOnce(new ApiError(409, 'Conflict', 'VERSION_CONFLICT'))
    const w = mountView()
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
    const w = mountView()
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
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).onDelete()
    expect(confirmRequire.mock.calls[0][0].message).toContain('restore')
  })

  it('delete on a non-soft collection keeps the irreversible confirm', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores() // meta has no softDelete
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).onDelete()
    expect(confirmRequire.mock.calls[0][0].message).toContain('cannot be undone')
  })

  // ---- FE-5: dirty-state leave guard --------------------------------------

  it('registers a route-leave guard synchronously on setup', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    mountView()
    expect(typeof leaveGuard).toBe('function')
  })

  it('route-leave guard resolves true without confirming when the form is clean', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('route-leave guard confirms when dirty; accept resolves true, reject resolves false', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'edited'

    const accepted = Promise.resolve(leaveGuard!())
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    expect(confirmRequire.mock.calls[0][0].header).toBe('Unsaved changes')
    confirmRequire.mock.calls[0][0].accept()
    await expect(accepted).resolves.toBe(true)

    const rejected = Promise.resolve(leaveGuard!())
    expect(confirmRequire).toHaveBeenCalledTimes(2)
    confirmRequire.mock.calls[1][0].reject()
    await expect(rejected).resolves.toBe(false)
  })

  it('re-baselines after a successful submit so leaving does not prompt', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5' })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'edited' // now dirty
    await (w.vm as any).onSubmit() // success -> re-baseline before navigate
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('re-baselines after Reload latest so leaving does not prompt', async () => {
    // reloadLatest() overwrites the model with the server copy via setModel(), which re-snaps the
    // baseline. The form is then clean, so the leave guard must resolve true without confirming.
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const get = vi.spyOn(itemsApi, 'get')
      .mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    vi.spyOn(itemsApi, 'update').mockRejectedValueOnce(new ApiError(409, 'Conflict', 'VERSION_CONFLICT'))
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'my-edit' // dirty
    get.mockResolvedValueOnce({ id: '5', status: 'server-copy', translations: {}, version: 9 })
    await (w.vm as any).onSubmit() // 409 -> conflict recovery, still dirty
    ;(w.vm as any).reloadLatest() // full overwrite -> re-baseline -> clean
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('re-baselines after delete-accept so the subsequent navigation does not prompt the guard', async () => {
    // onDelete's accept removes the item then captureBaseline()s before navigating: nothing is left
    // to lose, so the leave guard fired by that navigation must resolve true without confirming.
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    vi.spyOn(itemsApi, 'remove').mockResolvedValue(undefined)
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'my-edit' // dirty before delete
    ;(w.vm as any).onDelete()
    await confirmRequire.mock.calls[0][0].accept() // delete confirm -> remove + re-baseline + push
    confirmRequire.mockClear() // ignore the delete confirm; assert only the leave guard below
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  // ---- fold-in (c): Esc/X dismiss must settle the leave-guard promise ------

  it('leave guard: dismiss via onHide (Esc/backdrop/X) resolves the promise false', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'edited' // dirty

    const p = Promise.resolve(leaveGuard!())
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    const opts = confirmRequire.mock.calls[0][0]
    expect(typeof opts.onHide).toBe('function')
    // Dismissing fires neither accept nor reject; only onHide. The promise must still settle so the
    // router is not left awaiting forever. Assert via a timeout race: onHide -> resolves(false).
    opts.onHide()
    const settled = await Promise.race([
      p,
      new Promise((resolve) => setTimeout(() => resolve('PENDING'), 50)),
    ])
    expect(settled).toBe(false)
  })

  // ---- NAV-1: same-record (params-only) navigation dirty guard -------------

  it('registers a route-update guard synchronously on setup', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    mountView()
    expect(typeof updateGuard).toBe('function')
  })

  it('route-update guard: params (id) change while dirty prompts; reject resolves false', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'edited' // dirty

    const to = { params: { name: 'article', id: '6' } }
    const from = { params: { name: 'article', id: '5' } }
    const p = Promise.resolve(updateGuard!(to, from))
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    expect(confirmRequire.mock.calls[0][0].header).toBe('Unsaved changes')
    confirmRequire.mock.calls[0][0].reject()
    await expect(p).resolves.toBe(false)
  })

  it('route-update guard: params change while clean resolves true without prompting', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init() // clean

    const to = { params: { name: 'article', id: '6' } }
    const from = { params: { name: 'article', id: '5' } }
    await expect(Promise.resolve(updateGuard!(to, from))).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('route-update guard: same id/name (e.g. query-only change) resolves true even when dirty', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'edited' // dirty

    const to = { params: { name: 'article', id: '5' } }
    const from = { params: { name: 'article', id: '5' } }
    await expect(Promise.resolve(updateGuard!(to, from))).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('adds a beforeunload listener on mount and removes it on unmount', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const addSpy = vi.spyOn(window, 'addEventListener')
    const removeSpy = vi.spyOn(window, 'removeEventListener')
    const w = mountView()
    await w.vm.init()
    expect(addSpy.mock.calls.some((c) => c[0] === 'beforeunload')).toBe(true)
    w.unmount()
    expect(removeSpy.mock.calls.some((c) => c[0] === 'beforeunload')).toBe(true)
  })

  it('beforeunload calls preventDefault only when the form is dirty', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const addSpy = vi.spyOn(window, 'addEventListener')
    const w = mountView()
    await w.vm.init()
    const handler = addSpy.mock.calls.find((c) => c[0] === 'beforeunload')![1] as (e: Event) => void

    const clean = { preventDefault: vi.fn(), returnValue: undefined } as unknown as Event
    handler(clean)
    expect((clean as unknown as { preventDefault: ReturnType<typeof vi.fn> }).preventDefault).not.toHaveBeenCalled()

    ;(w.vm as any).model.shared.status = 'edited'
    const dirty = { preventDefault: vi.fn(), returnValue: undefined } as unknown as Event
    handler(dirty)
    expect((dirty as unknown as { preventDefault: ReturnType<typeof vi.fn> }).preventDefault).toHaveBeenCalled()
  })

  it('renders the page-head title from i18n in edit mode', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    expect(w.get('.page-head h1').text()).toBe('Edit Article')
  })
  it('shows the conflict banner text from i18n when a version conflict is armed', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const get = vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {}, version: 1 })
    vi.spyOn(itemsApi, 'update').mockRejectedValueOnce(new ApiError(409, 'Conflict', 'VERSION_CONFLICT'))
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'my-edit'
    get.mockResolvedValueOnce({ id: '5', status: 'x', translations: {}, version: 9 })
    await (w.vm as any).onSubmit()
    expect(w.get('.conflict-banner').text()).toContain('changed by someone else')
  })

  // ---- FE-R7: revision history drawer --------------------------------------

  it('renders the history drawer only for revisioned collections in edit mode', async () => {
    routeParams = { name: 'article', id: '5' }
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue({ ...meta, revisions: true })
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    expect(w.find('revision-history-drawer-stub').exists()).toBe(true)
  })

  it('does not render the history drawer when the collection is not revisioned', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores() // meta has no revisions flag
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    expect(w.find('revision-history-drawer-stub').exists()).toBe(false)
  })

  it('does not render the history drawer in create mode even if revisioned', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue({ ...meta, revisions: true })
    const w = mountView()
    await w.vm.init()
    expect(w.find('revision-history-drawer-stub').exists()).toBe(false)
  })

  it('onReverted re-fetches the full item (translations/relations) instead of trusting the emitted payload, and re-baselines', async () => {
    routeParams = { name: 'article', id: '5' }
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue({ ...meta, revisions: true })
    const get = vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'published', translations: {}, version: 1 })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'edited' // make dirty first so re-baseline is meaningful
    get.mockResolvedValueOnce({ id: '5', status: 'reverted-status', translations: {}, version: 4 })
    await (w.vm as any).onReverted()
    expect((w.vm as any).model.shared.status).toBe('reverted-status')
    expect((w.vm as any).model.version).toBe(4)
    // re-baselined: leaving must not prompt
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  // ---- Task 5: language list refresh after editing the Language collection -------

  const languageMeta = { name: 'language', label: 'Language', fields: [
    { name: 'code', label: 'Code', interface: 'text', required: true, searchable: false, sortable: false,
      readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false },
  ], relations: [] }

  it('saving a language item reloads the language store', async () => {
    routeParams = { name: 'language' }; routeName = 'collection-create'
    const auth = useAuthStore()
    auth.user = { id: '1', isSuperAdmin: true, permissions: {} }
    const schema = useSchemaStore()
    schema.load = vi.fn().mockResolvedValue(undefined)
    schema.get = vi.fn().mockReturnValue(languageMeta) as never
    // Deliberately do NOT stub languageStore.load here: this test exercises the real load/reload
    // implementation so the refetch-on-save can be observed at the languagesApi boundary.
    vi.spyOn(languagesApi, 'getEnabled').mockResolvedValue([
      { code: 'en', name: 'English', isDefault: true },
    ])
    vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: 'new-lang' })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.code = 'fr'
    const callsBefore = vi.mocked(languagesApi.getEnabled).mock.calls.length
    await (w.vm as any).onSubmit()
    await flushPromises()
    expect(vi.mocked(languagesApi.getEnabled).mock.calls.length).toBeGreaterThan(callsBefore)
  })

  it('does not reload the language store when saving a non-language collection', async () => {
    routeParams = { name: 'article' }; routeName = 'collection-create'
    setupStores()
    vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: '9' })
    const reloadSpy = vi.spyOn(useLanguageStore(), 'reload')
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'draft'
    await (w.vm as any).onSubmit()
    expect(reloadSpy).not.toHaveBeenCalled()
  })

  it('deleting a language item reloads the language store', async () => {
    routeParams = { name: 'language', id: '5' }
    const auth = useAuthStore()
    auth.user = { id: '1', isSuperAdmin: true, permissions: {} }
    const schema = useSchemaStore()
    schema.load = vi.fn().mockResolvedValue(undefined)
    schema.get = vi.fn().mockReturnValue(languageMeta) as never
    vi.spyOn(languagesApi, 'getEnabled').mockResolvedValue([
      { code: 'en', name: 'English', isDefault: true },
    ])
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', code: 'en', translations: {} })
    vi.spyOn(itemsApi, 'remove').mockResolvedValue(undefined)
    const w = mountView()
    await w.vm.init()
    const callsBefore = vi.mocked(languagesApi.getEnabled).mock.calls.length
    ;(w.vm as any).onDelete()
    await confirmRequire.mock.calls[0][0].accept()
    await flushPromises()
    expect(vi.mocked(languagesApi.getEnabled).mock.calls.length).toBeGreaterThan(callsBefore)
  })

  // ---- Task 8: RBAC editors mounted on the generic form (Role -> matrix, User -> effective) ----

  const roleMeta = { name: 'role', label: 'Role', fields: [
    { name: 'name', label: 'Name', interface: 'text', required: true, searchable: false, sortable: false,
      readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false },
  ], relations: [] }

  const userMeta = { name: 'user', label: 'User', fields: [
    { name: 'email', label: 'Email', interface: 'text', required: true, searchable: false, sortable: false,
      readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false },
  ], relations: [] }

  it('mounts PermissionMatrix only when editing a role as super-admin', async () => {
    vi.mocked(rbacApi.getRolePermissions).mockResolvedValue([])

    routeParams = { name: 'role', id: 'r1' }; routeName = 'collection-item'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(roleMeta)
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'r1', name: 'editor', isSuperAdmin: false })
    const editing = mountView()
    await editing.vm.init()
    await flushPromises()
    expect(editing.findComponent(PermissionMatrix).exists()).toBe(true)

    routeParams = { name: 'role' }; routeName = 'collection-create' // create mode: no id yet -> no matrix
    const creating = mountView()
    await creating.vm.init()
    await flushPromises()
    expect(creating.findComponent(PermissionMatrix).exists()).toBe(false)

    routeParams = { name: 'role', id: 'r1' }; routeName = 'collection-item'
    const { schema: schemaNonAdmin } = setupStores({ superAdmin: false })
    ;(schemaNonAdmin.get as any).mockReturnValue(roleMeta)
    useAuthStore().user = {
      id: 'u2', isSuperAdmin: false, permissions: { role: { read: true, write: false, delete: false } },
    }
    const nonAdmin = mountView()
    await nonAdmin.vm.init()
    await flushPromises()
    expect(nonAdmin.findComponent(PermissionMatrix).exists()).toBe(false)
  })

  it('mounts EffectivePermissionsPanel when editing a user as super-admin, and saving reloads it', async () => {
    vi.mocked(rbacApi.getEffectivePermissions).mockResolvedValue({ isSuperAdmin: false, permissions: {} })

    routeParams = { name: 'user', id: 'u9' }; routeName = 'collection-item'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(userMeta)
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'u9', email: 'x@struo.test', translations: {} })
    vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: 'u9' })

    const w = mountView()
    await w.vm.init()
    await flushPromises()
    const panel = w.findComponent(EffectivePermissionsPanel)
    expect(panel.exists()).toBe(true)
    expect(rbacApi.getEffectivePermissions).toHaveBeenCalledWith('u9')

    const callsBeforeSubmit = vi.mocked(rbacApi.getEffectivePermissions).mock.calls.length
    await (w.vm as any).onSubmit()
    await flushPromises()
    expect(vi.mocked(rbacApi.getEffectivePermissions).mock.calls.length).toBeGreaterThan(callsBeforeSubmit)
  })
})
