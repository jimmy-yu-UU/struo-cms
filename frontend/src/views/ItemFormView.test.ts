import { describe, it, expect, vi, beforeEach } from 'vitest'
import { nextTick } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import ItemFormView from './ItemFormView.vue'
import PermissionMatrix from '../components/rbac/PermissionMatrix.vue'
import EffectivePermissionsPanel from '../components/rbac/EffectivePermissionsPanel.vue'
import ChangePasswordDialog from '../components/account/ChangePasswordDialog.vue'
import JunctionLinksEditor from '../components/fields/JunctionLinksEditor.vue'
import { itemsApi } from '../api/itemsApi'
import { ApiError } from '../api/apiClient'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { languagesApi } from '../api/languagesApi'
import { rbacApi } from '../api/rbacApi'
import type { ConfirmRequest } from '@/composables/useConfirm'

// PermissionMatrix / EffectivePermissionsPanel are mounted for real (not stubbed)
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
// The local confirm resolves a Promise instead of taking callbacks, so the mock has to be a
// promise-returning spy whose resolution each test controls. Returning a bare vi.fn() would
// resolve undefined, which reads as "rejected" and would make every accept-path test pass for
// the wrong reason.
const confirmRequire = vi.fn<(req: ConfirmRequest) => Promise<boolean>>(() => Promise.resolve(true))
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
// Capture toast.add calls so the "grants save failed after create" warning can be
// asserted directly, same way confirm.require is captured above.
const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

const meta = { name: 'article', label: 'Article', fields: [
  { name: 'status', label: 'Status', interface: 'text', required: true, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false },
], relations: [
  { name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false },
  { name: 'comments', label: 'Comments', kind: 'oneToMany', targetCollection: 'comment', interface: 'relatedList', foreignKey: 'ArticleId', displayTemplate: '{Body}', editable: false, selfReferencing: false },
]}
// The single app-wide ConfirmHost lives in AppShell, not here, so ConfirmDialog is not part of this
// stub map. renderStubDefaultSlot lets the Save/History Button stubs render their label text so the
// button assertions below can read it.
// teleport: ChangePasswordDialog's vendored Dialog renders through reka's DialogPortal (built on
// Vue's own Teleport) once opened; stubbed the same way UserMenu.test.ts / ChangePasswordDialog.test.ts
// stub it, so a real teleport target absent from this test's jsdom tree never drops dialog content.
const stubs = { ItemForm: true, Button: true, RevisionHistoryDrawer: true, teleport: true }

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { itemForm: {
    loading: 'Loading…', collectionNotFound: 'Collection not found', itemNotFound: 'Item not found',
    noCreatePermission: "You don't have permission to create items here",
    new: 'New {label}', edit: 'Edit {label}', delete: 'Delete', save: 'Save', saving: 'Saving…', back: 'Back to list',
    translatableBadge: 'Translatable',
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
      grantsSaveFailedAfterCreate: "The role was created, but saving its permissions failed — retry from the role's edit page.",
    },
    password: { resetTitle: 'Reset password' },
  } },
})
function mountView() {
  return mount(ItemFormView, {
    global: { plugins: [i18n], stubs, renderStubDefaultSlot: true },
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
    confirmRequire.mockReset(); confirmRequire.mockResolvedValue(true)
    toastAdd.mockClear()
    vi.mocked(rbacApi.getRolePermissions).mockClear()
    vi.mocked(rbacApi.putRolePermissions).mockClear()
    vi.mocked(rbacApi.getEffectivePermissions).mockClear()
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

  it('carries the loaded version through to the update payload (version-chain fix)', async () => {
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

  it('generic 409 CONFLICT (e.g. duplicate email) shows the banner and does NOT trigger conflict recovery', async () => {
    // Only optimistic-lock clashes carry code VERSION_CONFLICT. Other 409s (delete-restrict,
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
    confirmRequire.mockResolvedValueOnce(true)
    await (w.vm as any).onDelete()
    await flushPromises()
    expect(confirmRequire).toHaveBeenCalled()
    expect(rm).toHaveBeenCalledWith('article', '5')
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
  })

  it('delete on a soft-delete collection uses the move-to-trash confirm', async () => {
    routeParams = { name: 'article', id: '5' }
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue({ ...meta, softDelete: true })
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    vi.spyOn(itemsApi, 'remove').mockResolvedValue(undefined)
    const w = mountView()
    await w.vm.init()
    await (w.vm as any).onDelete()
    await flushPromises()
    expect(confirmRequire.mock.calls[0][0].message).toContain('restore')
  })

  it('delete on a non-soft collection keeps the irreversible confirm', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores() // meta has no softDelete
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    vi.spyOn(itemsApi, 'remove').mockResolvedValue(undefined)
    const w = mountView()
    await w.vm.init()
    await (w.vm as any).onDelete()
    await flushPromises()
    expect(confirmRequire.mock.calls[0][0].message).toContain('cannot be undone')
    // 'danger' is the confirmStore severity vocabulary (primary | danger), not the toast one
    // (success | info | warn | error) — it drives ConfirmHost's destructive accept-button styling.
    expect(confirmRequire.mock.calls[0][0].severity).toBe('danger')
  })

  it('cancelling the delete confirm skips the delete entirely', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const rm = vi.spyOn(itemsApi, 'remove').mockResolvedValue(undefined)
    confirmRequire.mockResolvedValueOnce(false)
    const w = mountView()
    await w.vm.init()
    await (w.vm as any).onDelete()
    await flushPromises()
    expect(rm).not.toHaveBeenCalled()
    expect(push).not.toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
  })

  // ---- ui/button migration: every PageHeader button carries its variant/size and a native
  // type="button", so a click inside the form can never fall through to an implicit submit. ----

  it('renders the back/history/delete/save buttons on ui/button with the right variant, size, and type', async () => {
    routeParams = { name: 'article', id: '5' }
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue({ ...meta, revisions: true })
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()

    const buttons = w.findAllComponents({ name: 'Button' })
    expect(buttons).toHaveLength(4) // back, history, delete, save

    const [back, history, del, save] = buttons
    expect(back.props('variant')).toBe('ghost')
    expect(back.props('size')).toBe('icon')
    expect(back.attributes('type')).toBe('button')
    expect(back.attributes('aria-label')).toBe('Back to list')

    expect(history.props('variant')).toBe('ghost')
    expect(history.attributes('type')).toBe('button')
    expect(history.text()).toContain('History')

    expect(del.props('variant')).toBe('destructive')
    expect(del.attributes('type')).toBe('button')

    expect(save.attributes('type')).toBe('button')
  })

  it('Save disables and swaps its label to "Saving…" while a submit is in flight, then reverts', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {}, version: 1 })
    // ui/button has no `loading` prop, so the in-flight state has to be proven through `disabled` +
    // a label swap instead — hold the update pending so both the mid-flight and settled DOM states
    // can be observed.
    let resolveUpdate!: (v: Record<string, unknown>) => void
    vi.spyOn(itemsApi, 'update').mockImplementation(
      () => new Promise((resolve) => { resolveUpdate = resolve }),
    )
    const w = mountView()
    await w.vm.init()
    const saveButton = () =>
      w.findAllComponents({ name: 'Button' }).find((b) => b.text() === 'Save' || b.text() === 'Saving…')!

    expect(saveButton().text()).toBe('Save')
    expect(saveButton().attributes('disabled')).toBe('false')

    const submitted = (w.vm as any).onSubmit()
    await nextTick()
    expect(saveButton().text()).toBe('Saving…')
    expect(saveButton().attributes('disabled')).toBe('true')

    resolveUpdate({ id: '5' })
    await submitted
    expect(saveButton().text()).toBe('Save')
    expect(saveButton().attributes('disabled')).toBe('false')
  })

  // ---- dirty-state leave guard --------------------------------------

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

    confirmRequire.mockResolvedValueOnce(true)
    const accepted = leaveGuard!()
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    expect(confirmRequire.mock.calls[0][0].header).toBe('Unsaved changes')
    await expect(accepted).resolves.toBe(true)

    confirmRequire.mockResolvedValueOnce(false)
    const rejected = leaveGuard!()
    expect(confirmRequire).toHaveBeenCalledTimes(2)
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
    await (w.vm as any).onDelete() // delete confirm accepted (default mock) -> remove + re-baseline + push
    await flushPromises()
    confirmRequire.mockClear() // ignore the delete confirm; assert only the leave guard below
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  // ---- Escape dismiss must settle the leave-guard promise ------

  // The local confirm has no onHide callback distinct from reject: confirmStore.ask() hands back a
  // single Promise per request and settles it exactly once, from whichever of ConfirmHost's Cancel
  // button or its AlertDialog's Escape handler fires first — both end up calling store.reject().
  // An AlertDialog is not backdrop-dismissible (reka hard-prevents pointerDownOutside and
  // interactOutside) and ConfirmHost renders no close button, so Escape is the only non-button
  // path to that outcome. From guardLeave()'s point of view, a dismiss and an explicit Cancel are
  // therefore the SAME observable outcome: confirm.require(...) resolves false — by design, since
  // there is no third "closed without answering" state to model separately, which is what
  // guarantees the router's awaited navigation can never hang on an unanswered dialog.
  it('leave guard: dismiss (Escape) resolves false — indistinguishable from an explicit reject', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'edited' // dirty

    confirmRequire.mockResolvedValueOnce(false)
    await expect(leaveGuard!()).resolves.toBe(false)
  })

  // ---- same-record (params-only) navigation dirty guard -------------

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

    confirmRequire.mockResolvedValueOnce(false)
    const to = { params: { name: 'article', id: '6' } }
    const from = { params: { name: 'article', id: '5' } }
    const p = updateGuard!(to, from)
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    expect(confirmRequire.mock.calls[0][0].header).toBe('Unsaved changes')
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
    // "Reload latest" also moved onto ui/button — same variant/size/type contract as the header buttons.
    const reloadBtn = w.get('.conflict-banner').findComponent({ name: 'Button' })
    expect(reloadBtn.props('variant')).toBe('outline')
    expect(reloadBtn.props('size')).toBe('sm')
    expect(reloadBtn.attributes('type')).toBe('button')
  })

  // ---- Revision history drawer --------------------------------------

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

  // ---- language list refresh after editing the Language collection -------

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
    await (w.vm as any).onDelete()
    await flushPromises()
    expect(vi.mocked(languagesApi.getEnabled).mock.calls.length).toBeGreaterThan(callsBefore)
  })

  // ---- RBAC editors mounted on the generic form (Role -> matrix, User -> effective) ----

  const roleMeta = { name: 'role', label: 'Role', fields: [
    { name: 'name', label: 'Name', interface: 'text', required: true, searchable: false, sortable: false,
      readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false },
  ], relations: [] }

  const userMeta = { name: 'user', label: 'User', fields: [
    { name: 'email', label: 'Email', interface: 'text', required: true, searchable: false, sortable: false,
      readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false },
  ], relations: [
    { name: 'roles', label: 'Roles', kind: 'manyToMany', targetCollection: 'role', interface: 'tagSelect', foreignKey: null, displayTemplate: '{Name}', editable: true, selfReferencing: false },
  ] }

  it('mounts PermissionMatrix (edit AND create) only for a role as super-admin', async () => {
    vi.mocked(rbacApi.getRolePermissions).mockResolvedValue([])

    routeParams = { name: 'role', id: 'r1' }; routeName = 'collection-item'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(roleMeta)
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'r1', name: 'editor', isSuperAdmin: false })
    const editing = mountView()
    await editing.vm.init()
    await flushPromises()
    expect(editing.findComponent(PermissionMatrix).exists()).toBe(true)

    // Create mode also mounts the matrix now (createMode buffer, no GET) so the user does
    // not have to save-then-reopen to grant permissions.
    routeParams = { name: 'role' }; routeName = 'collection-create'
    const creating = mountView()
    await creating.vm.init()
    await flushPromises()
    const createdMatrix = creating.findComponent(PermissionMatrix)
    expect(createdMatrix.exists()).toBe(true)
    expect(createdMatrix.props('createMode')).toBe(true)

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

  it('mounts EffectivePermissionsPanel when editing a user as super-admin, passing the current role selection', async () => {
    vi.mocked(rbacApi.getEffectivePermissions).mockResolvedValue({ isSuperAdmin: false, permissions: {} })

    routeParams = { name: 'user', id: 'u9' }; routeName = 'collection-item'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(userMeta)
    vi.spyOn(itemsApi, 'get').mockResolvedValue({
      id: 'u9', email: 'x@struo.test', translations: {},
      roles: [{ id: 'r1' }, { id: 'r2' }],
    })
    vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: 'u9' })

    const w = mountView()
    await w.vm.init()
    await flushPromises()
    const panel = w.findComponent(EffectivePermissionsPanel)
    expect(panel.exists()).toBe(true)
    expect(panel.props('roleIds')).toEqual(['r1', 'r2'])
    expect(rbacApi.getEffectivePermissions).toHaveBeenCalledWith('u9', ['r1', 'r2'])
  })

  // ---- admin password-reset action on the User form (super-admin, existing user row only) ----

  it('offers a reset-password action on an existing user row for a super admin', async () => {
    vi.mocked(rbacApi.getEffectivePermissions).mockResolvedValue({ isSuperAdmin: false, permissions: {} })

    routeParams = { name: 'user', id: 'u9' }; routeName = 'collection-item'
    const { schema } = setupStores() // superAdmin: true by default
    ;(schema.get as any).mockReturnValue(userMeta)
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'u9', email: 'x@struo.test', translations: {} })
    const w = mountView()
    await w.vm.init()
    await flushPromises()

    const resetBtn = w.findAllComponents({ name: 'Button' }).find((b) => b.text() === 'Reset password')
    expect(resetBtn).toBeDefined()
    expect(resetBtn!.attributes('type')).toBe('button') // must never fall through to an implicit submit

    const dialog = w.findComponent(ChangePasswordDialog)
    expect(dialog.exists()).toBe(true)
    expect(dialog.props('targetUserId')).toBe('u9')
  })

  it('hides the reset-password action for a non-super-admin', async () => {
    routeParams = { name: 'user', id: 'u9' }; routeName = 'collection-item'
    const { schema } = setupStores({ superAdmin: false })
    ;(schema.get as any).mockReturnValue(userMeta)
    useAuthStore().user = {
      id: 'u2', isSuperAdmin: false, permissions: { user: { read: true, write: true, delete: false } },
    }
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'u9', email: 'x@struo.test', translations: {} })
    const w = mountView()
    await w.vm.init()
    await flushPromises()

    // Positive anchor: the view did reach its normal rendered state (not stuck loading / not-found),
    // so the absence below is a real "hidden by the guard", not a vacuous pass from an early return.
    expect(w.get('.page-head h1').text()).toBe('Edit User')
    expect(w.findAllComponents({ name: 'Button' }).find((b) => b.text() === 'Reset password')).toBeUndefined()
    expect(w.findComponent(ChangePasswordDialog).exists()).toBe(false)
  })

  it('hides the reset-password action on the create form', async () => {
    routeParams = { name: 'user' }; routeName = 'collection-create'
    const { schema } = setupStores() // superAdmin: true, but create mode has no existing row
    ;(schema.get as any).mockReturnValue(userMeta)
    const w = mountView()
    await w.vm.init()
    await flushPromises()

    // Positive anchor: proves the create form actually rendered, so the absence below is the guard
    // excluding create mode, not the view stuck in a loading/error state.
    expect(w.get('.page-head h1').text()).toBe('New User')
    expect(w.findAllComponents({ name: 'Button' }).find((b) => b.text() === 'Reset password')).toBeUndefined()
    expect(w.findComponent(ChangePasswordDialog).exists()).toBe(false)
  })

  it('hides the reset-password action on a non-user collection', async () => {
    routeParams = { name: 'article', id: '5' }; routeName = 'collection-item'
    setupStores() // superAdmin: true, but this is the `article` collection, not `user`
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    await flushPromises()

    // Positive anchor: proves the article form actually rendered, so the absence below is the guard
    // excluding non-user collections, not the view stuck in a loading/error state.
    expect(w.get('.page-head h1').text()).toBe('Edit Article')
    expect(w.findAllComponents({ name: 'Button' }).find((b) => b.text() === 'Reset password')).toBeUndefined()
    expect(w.findComponent(ChangePasswordDialog).exists()).toBe(false)
  })

  // ---- unified leave guard + form Save flushes a dirty permission matrix ----

  it('leave guard fires once and covers a dirty matrix (unified guard)', async () => {
    vi.mocked(rbacApi.getRolePermissions).mockResolvedValue([])

    routeParams = { name: 'role', id: 'r1' }; routeName = 'collection-item'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(roleMeta)
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'r1', name: 'editor', isSuperAdmin: false })
    const w = mountView()
    await w.vm.init()
    await flushPromises()

    // The generic form itself stays clean; only the matrix is dirtied via its own exposed toggle.
    const matrixVm: any = w.findComponent(PermissionMatrix).vm
    matrixVm.toggle('article', 'write', true)
    expect(matrixVm.dirty).toBe(true)

    confirmRequire.mockResolvedValueOnce(true)
    const p = (w.vm as any).guardLeave()
    expect(confirmRequire).toHaveBeenCalledTimes(1) // ONE dialog, not two
    await expect(p).resolves.toBe(true)
  })

  it('form Save flushes a dirty matrix and blocks navigation when matrix save fails', async () => {
    vi.mocked(rbacApi.getRolePermissions).mockResolvedValue([])

    routeParams = { name: 'role', id: 'r1' }; routeName = 'collection-item'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(roleMeta)
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'r1', name: 'editor', isSuperAdmin: false })
    vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: 'r1' })
    const w = mountView()
    await w.vm.init()
    await flushPromises()

    const matrixVm: any = w.findComponent(PermissionMatrix).vm
    matrixVm.toggle('article', 'write', true)

    vi.mocked(rbacApi.putRolePermissions).mockRejectedValueOnce(new Error('boom'))
    await (w.vm as any).onSubmit()
    expect(rbacApi.putRolePermissions).toHaveBeenCalled()
    expect(push).not.toHaveBeenCalled() // matrix save failed -> stay on the page
    expect(matrixVm.dirty).toBe(true) // matrix still dirty; its own toast already fired

    vi.mocked(rbacApi.putRolePermissions).mockResolvedValueOnce([
      { collection: 'article', canRead: false, canWrite: true, canDelete: false },
    ])
    await (w.vm as any).onSubmit()
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'role' } })
  })

  // ---- failed edit-mode matrix flush must not discard the update result --
  // Before the fix, a failing matrix.save() caused onSubmit to `return` BEFORE captureBaseline()
  // and before refreshing model.version from the update response — leaving the FORM's own saved
  // edits marked dirty (bogus unsaved-changes prompt) and a retry echoing the stale version (bogus
  // 409 VERSION_CONFLICT against the user's own prior save).
  it('failed edit-mode matrix flush leaves the form clean and retry does not send a stale version', async () => {
    vi.mocked(rbacApi.getRolePermissions).mockResolvedValue([])

    routeParams = { name: 'role', id: 'r1' }; routeName = 'collection-item'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(roleMeta)
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'r1', name: 'editor', isSuperAdmin: false, version: 1 })
    const upd = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: 'r1', version: 8 })
    const w = mountView()
    await w.vm.init()
    await flushPromises()

    // Dirty BOTH the generic form and the matrix.
    ;(w.vm as any).model.shared.name = 'Editor Renamed'
    const matrixVm: any = w.findComponent(PermissionMatrix).vm
    matrixVm.toggle('article', 'write', true)

    vi.mocked(rbacApi.putRolePermissions).mockRejectedValueOnce(new Error('boom'))
    await (w.vm as any).onSubmit()

    // onSubmit returned without navigating — the matrix flush failed.
    expect(push).not.toHaveBeenCalled()
    expect(matrixVm.dirty).toBe(true) // matrix still dirty; its own toast already fired

    // The update DID succeed and must not be discarded: the version token is refreshed...
    expect((w.vm as any).model.version).toBe(8)
    // ...and the FORM itself is re-baselined (clean) even though the matrix is still dirty. Prove
    // this in isolation from the matrix's own dirty flag by clearing it directly, then checking the
    // unified leave guard: if the form baseline had NOT been refreshed, it would still prompt here.
    matrixVm.markFlushed()
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()

    // A retry (matrix flush now succeeding) must echo the REFRESHED version, not the stale
    // pre-save one — otherwise the user's own prior save would bounce off a bogus 409.
    vi.mocked(rbacApi.putRolePermissions).mockResolvedValueOnce([
      { collection: 'article', canRead: false, canWrite: true, canDelete: false },
    ])
    await (w.vm as any).onSubmit()
    expect(upd).toHaveBeenLastCalledWith('role', 'r1', expect.objectContaining({ version: 8 }))
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'role' } })
  })

  // ---- create-mode matrix, buffered and saved with the form -------

  it('mounts the permission matrix in role create mode as super-admin', async () => {
    routeParams = { name: 'role' }; routeName = 'collection-create'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(roleMeta)
    const w = mountView()
    await w.vm.init()
    await flushPromises()
    const matrixComp = w.findComponent(PermissionMatrix)
    expect(matrixComp.exists()).toBe(true)
    expect(matrixComp.props('createMode')).toBe(true)
    expect(rbacApi.getRolePermissions).not.toHaveBeenCalled() // create mode: no GET
  })

  it('create submit with staged grants PUTs them against the id returned by create', async () => {
    routeParams = { name: 'role' }; routeName = 'collection-create'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(roleMeta)
    vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: 'new-role-1' })
    vi.mocked(rbacApi.putRolePermissions).mockResolvedValue([])
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.name = 'Editor'

    const matrixVm: any = w.findComponent(PermissionMatrix).vm
    matrixVm.toggle('article', 'read', true)

    await (w.vm as any).onSubmit()
    expect(rbacApi.putRolePermissions).toHaveBeenCalledWith('new-role-1', [
      { collection: 'article', canRead: true, canWrite: false, canDelete: false },
    ])
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'role' } })
  })

  it('create submit with an empty matrix buffer never calls putRolePermissions', async () => {
    routeParams = { name: 'role' }; routeName = 'collection-create'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(roleMeta)
    vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: 'new-role-2' })
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.name = 'Editor'

    await (w.vm as any).onSubmit()
    expect(rbacApi.putRolePermissions).not.toHaveBeenCalled()
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'role' } })
  })

  it('create submit: grants PUT rejection warns and routes to the new role edit page instead of the list, with no unhandled rejection', async () => {
    routeParams = { name: 'role' }; routeName = 'collection-create'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(roleMeta)
    vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: 'new-role-3' })
    vi.mocked(rbacApi.putRolePermissions).mockRejectedValueOnce(new Error('boom'))
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.name = 'Editor'

    const matrixVm: any = w.findComponent(PermissionMatrix).vm
    matrixVm.toggle('article', 'read', true)

    await (w.vm as any).onSubmit() // must not throw / leave an unhandled rejection
    expect(rbacApi.putRolePermissions).toHaveBeenCalledWith('new-role-3', [
      { collection: 'article', canRead: true, canWrite: false, canDelete: false },
    ])
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({
      severity: 'warn',
      summary: "The role was created, but saving its permissions failed — retry from the role's edit page.",
      life: 6000,
    }))
    expect(push).toHaveBeenCalledWith({ name: 'collection-item', params: { name: 'role', id: 'new-role-3' } })
    expect(push).not.toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'role' } })
  })

  // ---- Matrix baseline must be re-synced after the create-path flush, or the
  // unified leave guard fires a bogus "Unsaved changes" prompt right after a successful/failed
  // create-and-flush, which can trap the user on the create form (risking a duplicate role). ----

  it('after a successful create-path grants flush, the leave guard resolves true without confirming', async () => {
    routeParams = { name: 'role' }; routeName = 'collection-create'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(roleMeta)
    vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: 'new-role-4' })
    vi.mocked(rbacApi.putRolePermissions).mockResolvedValue([])
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.name = 'Editor'

    const matrixVm: any = w.findComponent(PermissionMatrix).vm
    matrixVm.toggle('article', 'read', true)
    expect(matrixVm.dirty).toBe(true)

    await (w.vm as any).onSubmit()
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'role' } })
    expect(matrixVm.dirty).toBe(false) // re-baselined by markFlushed()

    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('after a failed create-path grants flush, the leave guard does not block the edit-route push', async () => {
    routeParams = { name: 'role' }; routeName = 'collection-create'
    const { schema } = setupStores()
    ;(schema.get as any).mockReturnValue(roleMeta)
    vi.spyOn(itemsApi, 'create').mockResolvedValue({ id: 'new-role-5' })
    vi.mocked(rbacApi.putRolePermissions).mockRejectedValueOnce(new Error('boom'))
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.name = 'Editor'

    const matrixVm: any = w.findComponent(PermissionMatrix).vm
    matrixVm.toggle('article', 'read', true)

    await (w.vm as any).onSubmit()
    expect(push).toHaveBeenCalledWith({ name: 'collection-item', params: { name: 'role', id: 'new-role-5' } })
    expect(matrixVm.dirty).toBe(false) // re-baselined by markFlushed() despite the PUT failure

    // The guard must resolve true quietly — it must not block the very navigation the failure
    // path just performed.
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('loads a payload-bearing relation into JunctionLinksEditor and submits {id, ...payload} objects', async () => {
    routeParams = { name: 'article', id: 'a1' }
    const noteField = { name: 'note', label: 'Note', interface: 'text', required: false, searchable: false,
      sortable: false, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false }
    const nameField = { name: 'name', label: 'Name', interface: 'text', required: false, searchable: false,
      sortable: false, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false }
    const tagsRel = {
      name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag', interface: 'tagSelect',
      foreignKey: null, displayTemplate: null, editable: true, selfReferencing: false,
      junctionCollection: 'articleTag', junctionPayloadFields: ['note'], sortField: 'sort',
    }
    const articleMeta = { name: 'article', label: 'Article', fields: [], relations: [tagsRel] }
    const articleTagMeta = { name: 'articleTag', label: 'Article tag', hidden: true, fields: [noteField], relations: [] }
    const tagMeta = { name: 'tag', label: 'Tag', defaultDisplayField: 'name', fields: [nameField], relations: [] }

    const auth = useAuthStore()
    auth.user = {
      id: '1', isSuperAdmin: false, permissions: {
        article: { read: true, write: true, delete: false },
        articleTag: { read: true, write: true, delete: false },
        tag: { read: true, write: true, delete: false },
      },
    }
    const schema = useSchemaStore()
    schema.load = vi.fn().mockResolvedValue(undefined)
    schema.get = vi.fn((n: string) =>
      (n === 'article' ? articleMeta : n === 'articleTag' ? articleTagMeta : n === 'tag' ? tagMeta : undefined)) as never
    const lang = useLanguageStore()
    lang.load = vi.fn().mockResolvedValue(undefined)
    lang.languages = [{ code: 'en', name: 'English', isDefault: true }]

    vi.spyOn(itemsApi, 'get').mockResolvedValue({
      id: 'a1', version: 1, tags: [{ id: 't1', name: 'One', _junction: { note: 'hi' } }],
    })
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [{ id: 't1', name: 'One' }], total: 1 })

    const w = mount(ItemFormView, {
      global: { plugins: [i18n], stubs: { Combobox: true, RevisionHistoryDrawer: true, teleport: true, Button: true } },
    })
    await w.vm.init()
    await flushPromises()

    const editor = w.findComponent(JunctionLinksEditor)
    expect(editor.exists()).toBe(true)
    expect((editor.find('.junction-link input').element as HTMLInputElement).value).toBe('hi')
    await editor.find('.junction-link input').setValue('bye')

    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: 'a1', version: 2 })
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(update.mock.calls[0][2].tags).toEqual([{ id: 't1', note: 'bye' }])
  })
})
