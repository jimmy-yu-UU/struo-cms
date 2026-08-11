import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import UserMenu from './UserMenu.vue'
import { useAuthStore } from '../../stores/authStore'
import { i18n } from '../../i18n'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

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
})
