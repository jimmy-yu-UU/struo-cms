import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import UserMenu from './UserMenu.vue'
import ChangePasswordDialog from '../account/ChangePasswordDialog.vue'
import { useAuthStore } from '../../stores/authStore'
import { i18n } from '../../i18n'
import { ApiError } from '../../api/apiClient'
import { useSchemaStore } from '../../stores/schemaStore'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

function mountMenu() {
  // reka-ui's DropdownMenu portal is itself named "Teleport" -- see vitest.setup.ts for why
  // this stub configuration is required.
  return mount(UserMenu, {
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

async function openMenu(wrapper: ReturnType<typeof mountMenu>) {
  await wrapper.find('button').trigger('click')
  await flushPromises()
}

describe('UserMenu', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-TW'
  })

  // Redundant with the global restoreMocks configured in vite.config.ts, and kept only because
  // removing it here alone would leave the many other test files that do the same thing
  // inconsistent -- that sweep is registered as its own follow-up.
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows the super-admin role label', () => {
    const auth = useAuthStore()
    // A distinct name (not name/email absent) so displayName !== roleLabel — otherwise this
    // assertion would still pass even if the role <span> were deleted outright, since
    // displayName falls back to roleLabel when both name and email are missing.
    auth.user = { id: 'u1', name: '陳雅婷', isSuperAdmin: true, permissions: {} }
    const wrapper = mountMenu()
    const text = wrapper.find('button').text()
    expect(text).toContain('陳雅婷')
    expect(text).toContain('超級管理員')
  })

  it('shows the member role label for a non-super-admin', () => {
    const auth = useAuthStore()
    auth.user = { id: 'u2', name: '王小明', isSuperAdmin: false, permissions: {} }
    const wrapper = mountMenu()
    const text = wrapper.find('button').text()
    expect(text).toContain('王小明')
    expect(text).toContain('一般使用者')
  })

  it('shows the user name (fallback email) next to the avatar', () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', email: 'a@b.c', name: '陳雅婷', isSuperAdmin: true, permissions: {} }
    const wrapper = mountMenu()
    expect(wrapper.find('button').text()).toContain('陳雅婷')
  })

  it('falls back to email when name is missing', () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', email: 'a@b.c', name: null, isSuperAdmin: false, permissions: {} }
    const wrapper = mountMenu()
    expect(wrapper.find('button').text()).toContain('a@b.c')
  })

  it('the logout menu item logs out and routes to login', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    const logoutSpy = vi.spyOn(auth, 'logout').mockResolvedValue()
    const wrapper = mountMenu()

    await openMenu(wrapper)
    const item = wrapper.findAll('[role="menuitem"]').find((el) => el.text().includes('登出'))
    expect(item).toBeDefined()
    await item!.trigger('click')
    await flushPromises()

    expect(logoutSpy).toHaveBeenCalledOnce()
    expect(push).toHaveBeenCalledWith({ name: 'login' })
  })

  async function clickLogout() {
    const wrapper = mountMenu()
    await openMenu(wrapper)
    const item = wrapper.findAll('[role="menuitem"]').find((el) => el.text().includes('登出'))
    await item!.trigger('click')
    await flushPromises()
  }

  it('logout resets the per-user stores and shows no warning on success', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.spyOn(auth, 'logout').mockResolvedValue()
    const schema = useSchemaStore()
    schema.loaded = true
    await clickLogout()
    expect(schema.loaded).toBe(false)
    expect(toastAdd).not.toHaveBeenCalled()
  })

  it('a 401 from the logout request is a silent success that still ends on login', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.spyOn(auth, 'logout').mockResolvedValue() // the store maps 401 to success (authStore.test.ts)
    await clickLogout()
    expect(push).toHaveBeenCalledWith({ name: 'login' })
    expect(toastAdd).not.toHaveBeenCalled()
  })

  it('a server failure warns with a toast but still ends on login with the stores reset', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.spyOn(auth, 'logout').mockRejectedValue(new ApiError(500, 'boom'))
    const schema = useSchemaStore()
    schema.loaded = true
    await clickLogout()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'warn' }))
    expect(toastAdd.mock.calls[0][0].summary).toBeTruthy()
    expect(push).toHaveBeenCalledWith({ name: 'login' })
    expect(schema.loaded).toBe(false)
  })

  it('a network failure also warns and ends on login', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    vi.spyOn(auth, 'logout').mockRejectedValue(new TypeError('Failed to fetch'))
    await clickLogout()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'warn' }))
    expect(push).toHaveBeenCalledWith({ name: 'login' })
  })

  it('offers a change-password item that opens the dialog for the signed-in user', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'me', email: 'me@x', isSuperAdmin: false, permissions: {} }
    const wrapper = mountMenu()

    await openMenu(wrapper)
    const item = wrapper.findAll('[role="menuitem"]').find((el) => el.text().includes('變更密碼'))
    expect(item).toBeDefined()
    await item!.trigger('click')
    await flushPromises()

    const dialog = wrapper.findComponent(ChangePasswordDialog)
    expect(dialog.exists()).toBe(true)
    expect(dialog.props('open')).toBe(true)
    expect(dialog.props('targetUserId')).toBe('me')
  })

  it('does not render the change-password item when there is no signed-in user', async () => {
    const auth = useAuthStore()
    auth.user = null
    const wrapper = mountMenu()

    await openMenu(wrapper)
    const item = wrapper.findAll('[role="menuitem"]').find((el) => el.text().includes('變更密碼'))
    expect(item).toBeUndefined()
    expect(wrapper.findComponent(ChangePasswordDialog).exists()).toBe(false)
  })
})
