import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import { createI18n } from 'vue-i18n'
import en from '../../locales/en'
import PermissionMatrix from './PermissionMatrix.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { rbacApi } from '../../api/rbacApi'

vi.mock('../../api/rbacApi', () => ({
  rbacApi: { getRolePermissions: vi.fn(), putRolePermissions: vi.fn() },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountMatrix(props: { roleId?: string; isSuperAdminRole?: boolean; createMode?: boolean } = {}) {
  return mount(PermissionMatrix, {
    props: {
      roleId: props.roleId ?? 'r1',
      isSuperAdminRole: props.isSuperAdminRole ?? false,
      createMode: props.createMode ?? false,
    },
    global: { plugins: [PrimeVue, ToastService, ConfirmationService, i18n] },
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

  it('disables write/delete checkboxes for adminOnly collections', async () => {
    seedSchema()
    const w = mountMatrix()
    await flushPromises()
    const userRow = w.findAll('tbody tr').find((tr) => tr.text().includes('User'))!
    const boxes = userRow.findAllComponents({ name: 'Checkbox' })
    expect(boxes[0].props('disabled')).toBeFalsy() // read stays grantable
    expect(boxes[1].props('disabled')).toBe(true)
    expect(boxes[2].props('disabled')).toBe(true)
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

  // PermissionMatrix no longer owns a route-leave guard — ItemFormView owns the ONE guard
  // and folds in this component's `dirty` state instead. save() must report success/failure so the
  // parent form's Save can flush the matrix and know whether to keep the user on the page.
  it('save resolves true on success and false on failure', async () => {
    seedSchema()
    vi.mocked(rbacApi.putRolePermissions).mockResolvedValue([])
    const w = mountMatrix()
    await flushPromises()
    const vm: any = w.vm
    vm.toggle('article', 'write', true)
    await expect(vm.save()).resolves.toBe(true)
    vi.mocked(rbacApi.putRolePermissions).mockRejectedValue(new Error('boom'))
    vm.toggle('article', 'delete', true)
    await expect(vm.save()).resolves.toBe(false)
  })

  // ---- create-mode buffer (no GET, no own Save button, currentEntries()) ----

  it('createMode renders the table without a GET and without the save button', async () => {
    seedSchema()
    const w = mountMatrix({ createMode: true })
    await flushPromises()
    expect(w.find('table').exists()).toBe(true)
    expect(rbacApi.getRolePermissions).not.toHaveBeenCalled()
    expect(w.findComponent({ name: 'Button' }).exists()).toBe(false)
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
