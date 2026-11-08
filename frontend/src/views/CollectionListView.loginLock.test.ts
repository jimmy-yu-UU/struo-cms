import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
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
        column: 'Login lock', notLocked: 'Not locked', unlockWithTime: 'Unlock ({time})',
        lockedWithTime: 'Locked ({time})', unlocking: 'Unlocking…',
        lockedTip: 'Locked; unlocks automatically in {time}', tooltipWithReason: '{tip}. {reason}',
        spokenSeconds: '{s} s', spokenMinutes: '{m} min {s} s', spokenHours: '{h} h {m} min {s} s',
        readOnlyHint: 'Write permission on users is needed to clear a lock.',
        ariaNotLocked: '{email} is not locked', ariaLocked: '{email} is locked, {time} left',
        ariaUnlock: 'Unlock {email}, {time} left',
        unavailable: 'Unavailable', unavailableHint: 'The lock state could not be loaded.',
        superAdminOnly: 'Only a super-admin can clear this lock.', confirmHeader: 'Clear login lock',
        confirmMessage: 'Clear the login lock on {email}?', unlocked: 'Lock cleared', unlockFailed: 'Failed to clear the lock',
      },
      superAdmin: {
        deleteAccountHint: 'Only a super-admin can delete a super-admin account.',
        roleUndeletable: 'A super-admin role cannot be deleted.',
        publicRoleUndeletable: 'The public role cannot be deleted.',
      },
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
    // Only the interval and the clock: flushPromises keeps its real scheduler.
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    setActivePinia(createPinia())
    vi.clearAllMocks()
    confirmRequire.mockReset()
    confirmRequire.mockResolvedValue(true)
    vi.mocked(itemsApi.list).mockResolvedValue({ data: ROWS, total: ROWS.length })
    answerQuery({ 'locked@struo.test': lock('locked@struo.test', true, true, 90) })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  // The tooltip sits on the span around the button, because a disabled button receives no pointer events.
  const tipOf = (button: { element: Element }) => button.element.parentElement!.getAttribute('title')

  const tick = async (ms: number) => {
    vi.advanceTimersByTime(ms)
    await flushPromises()
  }

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

  it('shows one disabled Not locked button for every account that is not locked', async () => {
    seed('user', { superAdmin: true })
    const w = mountView()
    await flushPromises()

    const none = w.findAll('[data-testid="lock-none"]')
    expect(none).toHaveLength(2)
    expect(none[0].text()).toBe('Not locked')
    expect(none[0].attributes('disabled')).toBeDefined()
    expect(none[0].attributes('aria-label')).toBe('free@struo.test is not locked')
  })

  it('shows an enabled warning Unlock button with the time left for a lock the caller can clear', async () => {
    seed('user', { superAdmin: false, grants: WRITER })
    const w = mountView()
    await flushPromises()

    const button = w.get('[data-testid="lock-unlock"]')
    expect(button.text()).toBe('Unlock (1:30)')
    expect(button.attributes('disabled')).toBeUndefined()
    expect(button.classes().join(' ')).toContain('border-warning')
    expect(tipOf(button)).toBe('Locked; unlocks automatically in 1 min 30 s')
    expect(button.attributes('aria-label')).toBe('Unlock locked@struo.test, 1:30 left')
    expect(w.find('[data-testid="lock-badge"]').exists()).toBe(false)
  })

  it('shows a disabled Locked button, with the reason, to a caller without write on user', async () => {
    seed('user', { superAdmin: false, grants: { read: true, write: false, delete: false } })
    const w = mountView()
    await flushPromises()

    const button = w.get('[data-testid="lock-locked"]')
    expect(button.text()).toBe('Locked (1:30)')
    expect(button.attributes('disabled')).toBeDefined()
    expect(tipOf(button)).toBe(
      'Locked; unlocks automatically in 1 min 30 s. Write permission on users is needed to clear a lock.')
    expect(button.attributes('aria-label')).toBe('locked@struo.test is locked, 1:30 left')
    expect(w.find('[data-testid="lock-unlock"]').exists()).toBe(false)
  })

  it('shows a disabled Locked button when the lock is not clearable by the caller', async () => {
    seed('user', { superAdmin: false, grants: WRITER })
    answerQuery({ 'locked@struo.test': lock('locked@struo.test', true, false, 90) })
    const w = mountView()
    await flushPromises()

    const button = w.get('[data-testid="lock-locked"]')
    expect(button.text()).toBe('Locked (1:30)')
    expect(button.attributes('disabled')).toBeDefined()
    expect(tipOf(button)).toBe(
      'Locked; unlocks automatically in 1 min 30 s. Only a super-admin can clear this lock.')
  })

  // The reason is also exposed to assistive technology: the tooltip sits on a span, which a keyboard
  // or screen-reader user never reaches. The countdown stays out of it, so it is never read twice.
  describe('reason for a locked, non-clearable button', () => {
    const reasonOf = (w: ReturnType<typeof mountView>, button: { attributes: (k: string) => string | undefined }) => {
      const id = button.attributes('aria-describedby')
      expect(id).toBeTruthy()
      return w.get(`#${id}`)
    }

    it('describes the button of a read-only caller with the read-only hint alone', async () => {
      seed('user', { superAdmin: false, grants: { read: true, write: false, delete: false } })
      const w = mountView()
      await flushPromises()
      const button = w.get('[data-testid="lock-locked"]')
      const reason = reasonOf(w, button)
      expect(reason.text()).toBe('Write permission on users is needed to clear a lock.')
      expect(reason.classes()).toContain('sr-only')
      expect(button.attributes('disabled')).toBeDefined()
      expect(button.classes().join(' ')).not.toContain('cursor-not-allowed')
    })

    it('describes a non-clearable lock with the super-admin-only reason alone', async () => {
      seed('user', { superAdmin: false, grants: WRITER })
      answerQuery({ 'locked@struo.test': lock('locked@struo.test', true, false, 90) })
      const w = mountView()
      await flushPromises()
      const reason = reasonOf(w, w.get('[data-testid="lock-locked"]'))
      expect(reason.text()).toBe('Only a super-admin can clear this lock.')
      expect(reason.text()).not.toMatch(/\d:\d\d/)
    })

    it('gives every locked row its own description id', async () => {
      seed('user', { superAdmin: false, grants: { read: true, write: false, delete: false } })
      answerQuery({
        'free@struo.test': lock('free@struo.test', true, true, 30),
        'locked@struo.test': lock('locked@struo.test', true, true, 90),
      })
      const w = mountView()
      await flushPromises()
      const ids = w.findAll('[data-testid="lock-locked"]').map((b) => b.attributes('aria-describedby'))
      expect(ids).toHaveLength(2)
      expect(new Set(ids).size).toBe(2)
      expect(w.findAll('.sr-only').filter((el) => ids.includes(el.attributes('id')))).toHaveLength(2)
    })

    it('adds no description to the clearable Unlock button', async () => {
      seed('user', { superAdmin: true })
      const w = mountView()
      await flushPromises()
      expect(w.get('[data-testid="lock-unlock"]').attributes('aria-describedby')).toBeUndefined()
    })
  })

  it('formats the time as m:ss, and as h:mm:ss from an hour on', async () => {
    seed('user', { superAdmin: true })
    answerQuery({
      'free@struo.test': lock('free@struo.test', true, true, 5),
      'locked@struo.test': lock('locked@struo.test', true, true, 1800),
      'root@struo.test': lock('root@struo.test', true, true, 3725),
    })
    const w = mountView()
    await flushPromises()

    expect(w.findAll('[data-testid="lock-unlock"]').map((b) => b.text()))
      .toEqual(['Unlock (0:05)', 'Unlock (30:00)', 'Unlock (1:02:05)'])
    expect(tipOf(w.findAll('[data-testid="lock-unlock"]')[2]))
      .toBe('Locked; unlocks automatically in 1 h 2 min 5 s')
  })

  it('counts down once a second from the time the query returned', async () => {
    seed('user', { superAdmin: true })
    const w = mountView()
    await flushPromises()

    await tick(1000)
    expect(w.get('[data-testid="lock-unlock"]').text()).toBe('Unlock (1:29)')
    await tick(29000)
    expect(w.get('[data-testid="lock-unlock"]').text()).toBe('Unlock (1:00)')
    expect(tipOf(w.get('[data-testid="lock-unlock"]'))).toBe('Locked; unlocks automatically in 1 min 0 s')
  })

  it('queries the page once when rows reach zero, and the button turns into Not locked', async () => {
    seed('user', { superAdmin: true })
    answerQuery({
      'locked@struo.test': lock('locked@struo.test', true, true, 2),
      'root@struo.test': lock('root@struo.test', true, true, 2),
    })
    const w = mountView()
    await flushPromises()
    expect(loginLocksApi.query).toHaveBeenCalledTimes(1)
    answerQuery({})

    await tick(1000)
    expect(loginLocksApi.query).toHaveBeenCalledTimes(1)
    await tick(1000)
    expect(loginLocksApi.query).toHaveBeenCalledTimes(2)
    await tick(5000)
    expect(loginLocksApi.query).toHaveBeenCalledTimes(2)
    expect(w.findAll('[data-testid="lock-none"]')).toHaveLength(3)
    expect(w.find('[data-testid="lock-unlock"]').exists()).toBe(false)
  })

  it('runs one timer only while a row is locked, and stops it on unmount', async () => {
    seed('user', { superAdmin: true })
    const w = mountView()
    await flushPromises()
    expect(vi.getTimerCount()).toBe(1)

    w.unmount()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('runs no timer when nothing is locked', async () => {
    seed('user', { superAdmin: true })
    answerQuery({})
    mountView()
    await flushPromises()

    expect(vi.getTimerCount()).toBe(0)
  })

  it('stops the timer as soon as the page or the filter changes, until the new page is queried', async () => {
    seed('user', { superAdmin: true })
    const w = mountView()
    await flushPromises()
    expect(vi.getTimerCount()).toBe(1)

    let finish!: (value: { data: typeof ROWS; total: number }) => void
    vi.mocked(itemsApi.list).mockReturnValue(new Promise((resolve) => { finish = resolve }))
    ;(w.vm as unknown as { onFilterApply: (spec: object) => void }).onFilterApply({})
    await flushPromises()
    expect(vi.getTimerCount()).toBe(0)

    finish({ data: ROWS, total: ROWS.length })
    await flushPromises()
    expect(vi.getTimerCount()).toBe(1)
  })

  it('stops the timer when a lock is cleared and nothing else is locked', async () => {
    seed('user', { superAdmin: true })
    vi.mocked(loginLocksApi.clear).mockResolvedValue(undefined)
    const w = mountView()
    await flushPromises()
    expect(vi.getTimerCount()).toBe(1)
    answerQuery({})

    await w.get('[data-testid="lock-unlock"]').trigger('click')
    await flushPromises()

    expect(vi.getTimerCount()).toBe(0)
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
    expect(w.find('[data-testid="lock-unlock"]').exists()).toBe(false)
    expect(w.findAll('[data-testid="lock-none"]')).toHaveLength(3)
  })

  it('shows Unlocking while the lock is being cleared', async () => {
    seed('user', { superAdmin: true })
    let finish!: () => void
    vi.mocked(loginLocksApi.clear).mockReturnValue(new Promise<void>((resolve) => { finish = resolve }))
    const w = mountView()
    await flushPromises()

    await w.get('[data-testid="lock-unlock"]').trigger('click')
    await flushPromises()

    const button = w.get('[data-testid="lock-unlock"]')
    expect(button.text()).toBe('Unlocking…')
    expect(button.attributes('disabled')).toBeDefined()
    finish()
    await flushPromises()
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
    expect(w.find('[data-testid="lock-unlock"]').exists()).toBe(true)
  })

  it('keeps the list when the query fails and marks the column unavailable, with no button and no timer', async () => {
    seed('user', { superAdmin: true })
    vi.mocked(loginLocksApi.query).mockRejectedValue(new Error('down'))
    const w = mountView()
    await flushPromises()

    expect(w.text()).toContain('locked@struo.test')
    expect(w.findAll('[data-testid="lock-unavailable"]')).toHaveLength(ROWS.length)
    expect(w.find('[data-testid="lock-unlock"]').exists()).toBe(false)
    expect(w.find('[data-testid="lock-none"]').exists()).toBe(false)
    expect(w.find('[role="alert"]').exists()).toBe(false)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('disables the row delete of a super-admin role and of the public role, with the reason as tooltip', async () => {
    seed('role', { superAdmin: false, grants: WRITER })
    vi.mocked(itemsApi.list).mockResolvedValue({
      data: [
        { id: '1', name: 'editor', isSuperAdmin: false },
        { id: '2', name: 'root', isSuperAdmin: true },
        { id: '3', name: 'public', isSuperAdmin: false },
      ],
      total: 3,
    })
    const w = mountView()
    await flushPromises()

    const deletes = w.findAll('[data-testid="row-delete"]')
    expect(deletes.map((d) => d.attributes('disabled') !== undefined)).toEqual([false, true, true])
    expect(deletes[1].attributes('title')).toBe('A super-admin role cannot be deleted.')
    expect(deletes[2].attributes('title')).toBe('The public role cannot be deleted.')
  })

  it('keeps the row delete of every other collection as it is', async () => {
    seed('article', { superAdmin: false, grants: WRITER })
    vi.mocked(itemsApi.list).mockResolvedValue({ data: [{ id: '1', name: 'public', isSuperAdmin: true }], total: 1 })
    const w = mountView()
    await flushPromises()

    expect(w.get('[data-testid="row-delete"]').attributes('disabled')).toBeUndefined()
  })
})
