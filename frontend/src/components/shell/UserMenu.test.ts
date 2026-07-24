import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import UserMenu from './UserMenu.vue'
import { useAuthStore } from '../../stores/authStore'
import { i18n } from '../../i18n'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('primevue/menu', () => ({
  default: {
    name: 'Menu',
    props: ['model', 'popup'],
    methods: { toggle() {} },
    template: '<div class="pv-menu" />',
  },
}))

function mountMenu() {
  return mount(UserMenu, { global: { plugins: [i18n] } })
}

describe('UserMenu', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-TW'
  })

  it('shows the super-admin role label', () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    const wrapper = mountMenu()
    expect(wrapper.find('.user-role').text()).toBe('超級管理員')
  })

  it('shows the member role label for a non-super-admin', () => {
    const auth = useAuthStore()
    auth.user = { id: 'u2', isSuperAdmin: false, permissions: {} }
    const wrapper = mountMenu()
    expect(wrapper.find('.user-role').text()).toBe('一般使用者')
  })

  it('the logout menu item logs out and routes to login', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    const logoutSpy = vi.spyOn(auth, 'logout').mockResolvedValue()
    const wrapper = mountMenu()
    const model = (
      wrapper.vm as unknown as {
        menuModel: { label: string; command: (event: { originalEvent?: { preventDefault: () => void } }) => void }[]
      }
    ).menuModel
    const logout = model.find((m) => m.label === '登出')!
    expect(logout).toBeTruthy()
    await logout.command({ originalEvent: { preventDefault: () => {} } })
    await new Promise((r) => setTimeout(r, 0))
    expect(logoutSpy).toHaveBeenCalledOnce()
    expect(push).toHaveBeenCalledWith({ name: 'login' })
  })

  it('prevents the anchor default action on the logout item, so a guard-suspended navigation is not cancelled by hash nav', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    const logoutSpy = vi.spyOn(auth, 'logout').mockResolvedValue()
    const wrapper = mountMenu()
    const model = (
      wrapper.vm as unknown as {
        menuModel: { label: string; command: (event: { originalEvent?: { preventDefault: () => void } }) => void }[]
      }
    ).menuModel
    const logout = model.find((m) => m.label === '登出')!

    const preventDefault = vi.fn()
    await logout.command({ originalEvent: { preventDefault } })
    await new Promise((r) => setTimeout(r, 0))

    expect(preventDefault).toHaveBeenCalledOnce()
    expect(logoutSpy).toHaveBeenCalledOnce()
  })

  it('shows the user name (fallback email) next to the avatar', () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', email: 'a@b.c', name: '陳雅婷', isSuperAdmin: true, permissions: {} }
    const wrapper = mountMenu()
    expect(wrapper.find('.user-name b').text()).toBe('陳雅婷')
  })

  it('falls back to email when name is missing', () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', email: 'a@b.c', name: null, isSuperAdmin: false, permissions: {} }
    const wrapper = mountMenu()
    expect(wrapper.find('.user-name b').text()).toBe('a@b.c')
  })
})
