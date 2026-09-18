import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import PermissionMatrix from './PermissionMatrix.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { rbacApi, type RolePermissionEntry } from '../../api/rbacApi'

vi.mock('../../api/rbacApi', () => ({
  rbacApi: { getRolePermissions: vi.fn(), putRolePermissions: vi.fn() },
}))

const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountMatrix(props: { roleId?: string; isSuperAdminRole?: boolean; createMode?: boolean } = {}) {
  return mount(PermissionMatrix, {
    props: {
      roleId: props.roleId ?? 'r1',
      isSuperAdminRole: props.isSuperAdminRole ?? false,
      createMode: props.createMode ?? false,
    },
    global: { plugins: [i18n] },
  })
}

function seedSchema() {
  useSchemaStore().collections = [
    { name: 'article', label: 'Article', group: 'Content', fields: [], relations: [] },
    { name: 'user', label: 'User', group: 'System', adminOnly: true, fields: [], relations: [] },
  ] as any
}

describe('PermissionMatrix', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    toastAdd.mockClear()
    vi.mocked(rbacApi.getRolePermissions).mockResolvedValue([
      { collection: 'article', canRead: true, canWrite: false, canDelete: false },
    ])
  })

  it('renders one row per collection with grants from the API', async () => {
    seedSchema()
    const w = mountMatrix()
    await flushPromises()
    expect(w.text()).toContain('Article')
    expect(w.text()).toContain('User')
    expect(rbacApi.getRolePermissions).toHaveBeenCalledWith('r1')
  })

  it('renders one vendored checkbox per collection-permission cell', async () => {
    seedSchema()
    const w = mountMatrix()
    await flushPromises()
    // Two collections x read/write/delete.
    expect(w.findAll('[data-slot="checkbox"]')).toHaveLength(6)
  })

  it('labels every checkbox with its collection and column, so a screen reader hears a distinct name', async () => {
    // Rows sort by group then label: article (Content) first, user (System) second.
    seedSchema()
    const w = mountMatrix()
    await flushPromises()
    const boxes = w.findAll('[data-slot="checkbox"]')
    expect(boxes[0].attributes('aria-label')).toBe('Article — Read')
    expect(boxes[1].attributes('aria-label')).toBe('Article — Write')
    expect(boxes[2].attributes('aria-label')).toBe('Article — Delete')
    expect(boxes[3].attributes('aria-label')).toBe('User — Read')
    expect(boxes[4].attributes('aria-label')).toBe('User — Write')
    expect(boxes[5].attributes('aria-label')).toBe('User — Delete')
  })

  it('reflects the loaded grants, including a reload after mount', async () => {
    seedSchema()
    const w = mountMatrix()
    await flushPromises()
    const articleRead = () => w.findAll('[data-slot="checkbox"]')[0]
    // beforeEach seeds article read=true.
    expect(articleRead().attributes('data-state')).toBe('checked')
    expect(articleRead().attributes('aria-checked')).toBe('true')
    // ItemFormView's create-then-flush path and a plain reload both replace the whole grants
    // record.
    vi.mocked(rbacApi.getRolePermissions).mockResolvedValue([
      { collection: 'article', canRead: false, canWrite: true, canDelete: false },
    ])
    await (w.vm as unknown as { load: () => Promise<void> }).load()
    await flushPromises()
    expect(articleRead().attributes('data-state')).toBe('unchecked')
    expect(w.findAll('[data-slot="checkbox"]')[1].attributes('data-state')).toBe('checked')
  })

  // `load()` toggles the `loading` flag around the whole table, which unmounts and remounts every
  // row (the table sits behind `v-else-if="!loading"`) — a fresh mount re-seeds an uncontrolled
  // control from its current `default-value`, so the test above cannot tell a controlled checkbox
  // from an uncontrolled one. `toggle()` is the exact handler wired to each checkbox's own
  // `@update:model-value`, and it mutates `grants` alone (never `loading`), so calling it directly
  // changes the prop feeding an ALREADY-MOUNTED checkbox without remounting anything. `save()`
  // reassigns `grants` from the PUT response the same way — no `loading` toggle either — so this is
  // also the only thing that would catch a server-normalised grant leaving a cell stale.
  // All three columns are driven, not just read: `write` and `delete` sit behind an identical
  // `:model-value` binding and are equally capable of silently going uncontrolled on their own.
  it('every column stays governed by the grants record after mount, not just at its initial render', async () => {
    seedSchema()
    const w = mountMatrix()
    await flushPromises()
    const cell = (i: number) => () => w.findAll('[data-slot="checkbox"]')[i]
    const [articleRead, articleWrite, articleDelete] = [cell(0), cell(1), cell(2)]
    expect(articleRead().attributes('data-state')).toBe('checked') // beforeEach seeds read=true
    expect(articleWrite().attributes('data-state')).toBe('unchecked')
    expect(articleDelete().attributes('data-state')).toBe('unchecked')

    const vm = w.vm as unknown as { toggle: (c: string, k: string, v: boolean) => void }
    vm.toggle('article', 'read', false)
    vm.toggle('article', 'write', true)
    vm.toggle('article', 'delete', true)
    await flushPromises()

    expect(articleRead().attributes('data-state')).toBe('unchecked')
    expect(articleRead().attributes('aria-checked')).toBe('false')
    expect(articleWrite().attributes('data-state')).toBe('checked')
    expect(articleWrite().attributes('aria-checked')).toBe('true')
    expect(articleDelete().attributes('data-state')).toBe('checked')
    expect(articleDelete().attributes('aria-checked')).toBe('true')
  })

  it('stores a grant emitted by the real Checkbox child on a real click', async () => {
    seedSchema()
    const w = mountMatrix()
    await flushPromises()
    // reka's CheckboxRoot toggles on a real click, so drive that rather than emitting from the
    // child — a real click also proves the control is reachable and not pointer-events-none.
    await w.findAll('[data-slot="checkbox"]')[2].trigger('click')
    await flushPromises()
    expect(w.findAll('[data-slot="checkbox"]')[2].attributes('data-state')).toBe('checked')
    expect((w.vm as unknown as { dirty: boolean }).dirty).toBe(true)
  })

  it('disables write and delete on an adminOnly collection but keeps read grantable', async () => {
    seedSchema()
    const w = mountMatrix()
    await flushPromises()
    const boxes = w.findAll('[data-slot="checkbox"]')
    expect(boxes[3].attributes('disabled')).toBeUndefined()
    expect(boxes[4].attributes('disabled')).toBeDefined()
    expect(boxes[5].attributes('disabled')).toBeDefined()
  })

  it('super-admin role shows a notice instead of the matrix', async () => {
    seedSchema()
    const w = mountMatrix({ isSuperAdminRole: true })
    await flushPromises()
    expect(w.find('table').exists()).toBe(false)
    expect(w.text()).toContain('super admin')
    expect(rbacApi.getRolePermissions).not.toHaveBeenCalled()
  })

  it('save PUTs only rows with at least one grant and resets dirty', async () => {
    seedSchema()
    vi.mocked(rbacApi.putRolePermissions).mockResolvedValue([
      { collection: 'article', canRead: true, canWrite: true, canDelete: false },
    ])
    const w = mountMatrix()
    await flushPromises()
    const vm: any = w.vm
    vm.toggle('article', 'write', true)
    vm.toggle('user', 'read', false) // stays all-false -> must not be sent
    await vm.save()
    expect(rbacApi.putRolePermissions).toHaveBeenCalledWith('r1', [
      { collection: 'article', canRead: true, canWrite: true, canDelete: false },
    ])
    expect(vm.dirty).toBe(false)
  })

  // PermissionMatrix does not own a route-leave guard — ItemFormView owns the ONE guard
  // and folds in this component's `dirty` state instead. save() must report success/failure so the
  // parent form's Save can flush the matrix and know whether to keep the user on the page. The
  // toast composable is the vendored/sonner one, mocked at module level.
  it('save resolves true on success and false on failure, and toasts success/failure accordingly', async () => {
    seedSchema()
    vi.mocked(rbacApi.putRolePermissions).mockResolvedValue([])
    const w = mountMatrix()
    await flushPromises()
    const vm: any = w.vm
    vm.toggle('article', 'write', true)
    await expect(vm.save()).resolves.toBe(true)
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }))
    toastAdd.mockClear()
    vi.mocked(rbacApi.putRolePermissions).mockRejectedValue(new Error('boom'))
    vm.toggle('article', 'delete', true)
    await expect(vm.save()).resolves.toBe(false)
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }))
  })

  it('the save button carries the vendored identity, a native type="button", :disabled tracking dirty/saving, and a saving label swap', async () => {
    seedSchema()
    let resolvePut!: (v: RolePermissionEntry[]) => void
    vi.mocked(rbacApi.putRolePermissions).mockImplementation(
      () => new Promise((resolve) => { resolvePut = resolve }),
    )
    const w = mountMatrix()
    await flushPromises()
    const btn = () => w.get('[data-slot="button"]')

    expect(btn().attributes('type')).toBe('button')
    expect(btn().text()).toBe('Save permissions')
    expect(btn().attributes('disabled')).toBeDefined() // clean -> disabled

    await w.findAll('[data-slot="checkbox"]')[1].trigger('click') // dirty the matrix
    expect(btn().attributes('disabled')).toBeUndefined() // dirty -> enabled

    await btn().trigger('click')
    expect(btn().text()).toBe('Saving permissions…')
    expect(btn().attributes('disabled')).toBeDefined() // saving -> disabled even though dirty

    resolvePut([{ collection: 'article', canRead: true, canWrite: true, canDelete: false }])
    await flushPromises()
    expect(btn().text()).toBe('Save permissions')
  })

  // ---- create-mode buffer (no GET, no own Save button, currentEntries()) ----

  it('createMode renders the table without a GET and without the save button', async () => {
    seedSchema()
    const w = mountMatrix({ createMode: true })
    await flushPromises()
    expect(w.find('table').exists()).toBe(true)
    expect(rbacApi.getRolePermissions).not.toHaveBeenCalled()
    expect(w.find('[data-slot="button"]').exists()).toBe(false)
  })

  it('currentEntries() returns only non-all-false rows after toggles, in create mode', async () => {
    seedSchema()
    const w = mountMatrix({ createMode: true })
    await flushPromises()
    const vm: any = w.vm
    vm.toggle('article', 'read', true)
    vm.toggle('article', 'write', true)
    vm.toggle('user', 'read', false) // stays all-false -> must not be included
    expect(vm.currentEntries()).toEqual([
      { collection: 'article', canRead: true, canWrite: true, canDelete: false },
    ])
  })
})
