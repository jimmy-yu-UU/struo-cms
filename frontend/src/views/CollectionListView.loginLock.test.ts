import { describe, it, expect, vi, beforeEach } from 'vitest'
import { reactive } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import CollectionListView from './CollectionListView.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { itemsApi } from '../api/itemsApi'
import { loginLocksApi } from '../api/loginLocksApi'
import { ApiError } from '../api/apiClient'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: {
    en: {
      collectionList: {
        count: '{n} items', new: 'New', active: 'Active', trash: 'Trash', edit: 'Edit', view: 'View',
        delete: 'Delete', restore: 'Restore', purge: 'Delete permanently', empty: 'No records',
        notFound: 'Collection not found', noAccess: "You don't have access to this collection",
        trashNotice: 'Trash', deletedAt: 'Deleted at', emptyTrash: 'Trash is empty',
      },
      loginLocks: {
        column: 'Login lock', lockedFor: 'Locked, ends in {time}', durationSeconds: '{n} s',
        durationMinutes: '{n} min', unlock: 'Unlock', unlocking: 'Unlocking…', unlockLabel: 'Unlock {email}',
        unavailable: 'Unavailable', unavailableHint: 'The lock state could not be loaded.',
        superAdminOnly: 'Only a super-admin can clear this lock.', confirmHeader: 'Clear login lock',
        confirmMessage: 'Clear the login lock on {email}?', unlocked: 'Lock cleared', unlockFailed: 'Failed to clear the lock',
      },
      superAdmin: { deleteAccountHint: 'Only a super-admin can delete a super-admin account.' },
      filterBuilder: {
        addCondition: 'Add condition', clear: 'Clear conditions', apply: 'Search', field: 'Field',
        operator: 'Operator', value: 'Value', remove: 'Remove condition',
        opEq: 'equals', opNeq: 'does not equal', opContains: 'contains',
      },
      common: {
        sortAscending: 'Sort ascending', sortDescending: 'Sort descending', clearSort: 'Clear sorting',
        previous: 'Previous page', next: 'Next page', columns: 'Columns', rowsPerPage: 'Rows per page',
        loadFailed: 'Load failed', actionFailed: 'Action failed',
        confirmDefaultHeader: 'Please confirm', confirmAccept: 'Confirm', confirmReject: 'Cancel',
      },
    },
  },
})

const pushMock = vi.fn()
const mockRoute = reactive({ params: { name: 'user' } })
vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
  useRouter: () => ({ push: pushMock }),
}))
vi.mock('../api/itemsApi', () => ({ itemsApi: { list: vi.fn(), remove: vi.fn(), restore: vi.fn() } }))
vi.mock('../api/schemaApi', () => ({ schemaApi: { getAll: vi.fn(), get: vi.fn() } }))
vi.mock('../api/loginLocksApi', () => ({ loginLocksApi: { query: vi.fn(), clear: vi.fn() } }))
const confirmRequire = vi.fn()
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

type Grant = { read: boolean; write: boolean; delete: boolean }

const ROWS = [
  { id: '1', email: 'free@struo.test' },
  { id: '2', email: 'locked@struo.test' },
  { id: '3', email: 'root@struo.test' },
]

function seed(collection: string, user: { superAdmin: boolean; grants?: Grant }) {
  const schema = useSchemaStore()
  schema.collections = [{
    name: collection, label: collection, defaultDisplayField: 'email', creatable: false, relations: [],
    fields: [{ name: 'email', label: 'Email', interface: 'text', required: true, searchable: true,
      sortable: true, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false }],
  }]
  schema.loaded = true
  const lang = useLanguageStore()
  lang.load = vi.fn().mockResolvedValue(undefined)
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  mockRoute.params.name = collection
  useAuthStore().user = {
    id: 'me', isSuperAdmin: user.superAdmin, permissions: user.grants ? { [collection]: user.grants } : {},
  }
}

const lock = (email: string, locked: boolean, clearable = true, retryAfterSeconds = 0) =>
  ({ email, locked, retryAfterSeconds: locked ? retryAfterSeconds : 0, clearable })

function answerQuery(map: Record<string, ReturnType<typeof lock>>) {
  vi.mocked(loginLocksApi.query).mockImplementation(async (emails: string[]) =>
    emails.map((e) => map[e] ?? lock(e, false)))
}

function mountView() {
  return mount(CollectionListView, { global: { plugins: [i18n] } })
}

const WRITER: Grant = { read: true, write: true, delete: true }

describe('CollectionListView login lock column', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    confirmRequire.mockReset()
    confirmRequire.mockResolvedValue(true)
    vi.mocked(itemsApi.list).mockResolvedValue({ data: ROWS, total: ROWS.length })
    answerQuery({ 'locked@struo.test': lock('locked@struo.test', true, true, 90) })
  })

  it('shows the column to a super-admin and queries once with the emails of the page', async () => {
    seed('user', { superAdmin: true })
    const w = mountView()
    await flushPromises()

    expect(w.text()).toContain('Login lock')
    expect(loginLocksApi.query).toHaveBeenCalledTimes(1)
    expect(loginLocksApi.query).toHaveBeenCalledWith(ROWS.map((r) => r.email))
  })

  it('shows the column to a caller with read on user', async () => {
    seed('user', { superAdmin: false, grants: { read: true, write: false, delete: false } })
    const w = mountView()
    await flushPromises()

    expect(w.text()).toContain('Login lock')
    expect(loginLocksApi.query).toHaveBeenCalledTimes(1)
  })

  it('has no column and asks nothing of a caller without read on user', async () => {
    seed('user', { superAdmin: false })
    const w = mountView()
    await flushPromises()

    expect(w.text()).toContain("You don't have access to this collection")
    expect(loginLocksApi.query).not.toHaveBeenCalled()
  })

  it('has no column and asks nothing on another collection', async () => {
    seed('article', { superAdmin: true })
    const w = mountView()
    await flushPromises()

    expect(w.text()).not.toContain('Login lock')
    expect(loginLocksApi.query).not.toHaveBeenCalled()
  })

  it('does not query for an empty page', async () => {
    seed('user', { superAdmin: true })
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
    mountView()
    await flushPromises()

    expect(loginLocksApi.query).not.toHaveBeenCalled()
  })

  it('shows a dash when not locked and the remaining time, rounded up to minutes, when locked', async () => {
    seed('user', { superAdmin: true })
    const w = mountView()
    await flushPromises()

    expect(w.findAll('[data-testid="lock-none"]')).toHaveLength(2)
    const badge = w.get('[data-testid="lock-badge"]')
    expect(badge.text()).toBe('Locked, ends in 2 min')
  })

  it('shows seconds under a minute', async () => {
    seed('user', { superAdmin: true })
    answerQuery({ 'locked@struo.test': lock('locked@struo.test', true, true, 45) })
    const w = mountView()
    await flushPromises()

    expect(w.get('[data-testid="lock-badge"]').text()).toBe('Locked, ends in 45 s')
  })

  it('hides the Unlock button from a caller without write on user', async () => {
    seed('user', { superAdmin: false, grants: { read: true, write: false, delete: false } })
    const w = mountView()
    await flushPromises()

    expect(w.find('[data-testid="lock-badge"]').exists()).toBe(true)
    expect(w.find('[data-testid="lock-unlock"]').exists()).toBe(false)
  })

  it('shows an enabled Unlock button to a caller with write on user', async () => {
    seed('user', { superAdmin: false, grants: WRITER })
    const w = mountView()
    await flushPromises()

    const button = w.get('[data-testid="lock-unlock"]')
    expect(button.attributes('disabled')).toBeUndefined()
    expect(button.attributes('aria-label')).toBe('Unlock locked@struo.test')
  })

  it('disables Unlock with a tooltip when the lock is not clearable by the caller', async () => {
    seed('user', { superAdmin: false, grants: WRITER })
    answerQuery({ 'locked@struo.test': lock('locked@struo.test', true, false, 90) })
    const w = mountView()
    await flushPromises()

    const button = w.get('[data-testid="lock-unlock"]')
    expect(button.attributes('disabled')).toBeDefined()
    expect(button.attributes('title')).toBe('Only a super-admin can clear this lock.')
  })

  it('disables the row delete for an account the caller cannot clear', async () => {
    seed('user', { superAdmin: false, grants: WRITER })
    answerQuery({ 'root@struo.test': lock('root@struo.test', false, false) })
    const w = mountView()
    await flushPromises()

    const deletes = w.findAll('[data-testid="row-delete"]')
    expect(deletes.map((d) => d.attributes('disabled') !== undefined)).toEqual([false, false, true])
    expect(deletes[2].attributes('title')).toBe('Only a super-admin can delete a super-admin account.')
  })

  it('asks for confirmation, clears the lock, toasts and queries the page again', async () => {
    seed('user', { superAdmin: true })
    vi.mocked(loginLocksApi.clear).mockResolvedValue(undefined)
    const w = mountView()
    await flushPromises()
    answerQuery({})

    await w.get('[data-testid="lock-unlock"]').trigger('click')
    await flushPromises()

    expect(confirmRequire).toHaveBeenCalledWith(expect.objectContaining({
      header: 'Clear login lock', message: 'Clear the login lock on locked@struo.test?', severity: 'danger',
    }))
    expect(loginLocksApi.clear).toHaveBeenCalledWith('locked@struo.test')
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success', summary: 'Lock cleared' }))
    expect(loginLocksApi.query).toHaveBeenCalledTimes(2)
    expect(w.find('[data-testid="lock-badge"]').exists()).toBe(false)
  })

  it('does nothing when the confirmation is cancelled', async () => {
    seed('user', { superAdmin: true })
    confirmRequire.mockResolvedValue(false)
    const w = mountView()
    await flushPromises()

    await w.get('[data-testid="lock-unlock"]').trigger('click')
    await flushPromises()

    expect(loginLocksApi.clear).not.toHaveBeenCalled()
    expect(loginLocksApi.query).toHaveBeenCalledTimes(1)
    expect(toastAdd).not.toHaveBeenCalled()
  })

  it('toasts the failure with its trace id when clearing fails, and keeps the lock shown', async () => {
    seed('user', { superAdmin: true })
    vi.mocked(loginLocksApi.clear).mockRejectedValue(new ApiError(500, 'boom', 'INTERNAL', undefined, undefined, 'trace-1'))
    const w = mountView()
    await flushPromises()

    await w.get('[data-testid="lock-unlock"]').trigger('click')
    await flushPromises()

    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({
      severity: 'error', summary: 'Failed to clear the lock', detail: expect.stringContaining('trace-1'),
    }))
    expect(w.find('[data-testid="lock-badge"]').exists()).toBe(true)
  })

  it('keeps the list when the query fails and marks the column unavailable', async () => {
    seed('user', { superAdmin: true })
    vi.mocked(loginLocksApi.query).mockRejectedValue(new Error('down'))
    const w = mountView()
    await flushPromises()

    expect(w.text()).toContain('locked@struo.test')
    expect(w.findAll('[data-testid="lock-unavailable"]')).toHaveLength(ROWS.length)
    expect(w.find('[data-testid="lock-badge"]').exists()).toBe(false)
    expect(w.find('[role="alert"]').exists()).toBe(false)
  })
})
