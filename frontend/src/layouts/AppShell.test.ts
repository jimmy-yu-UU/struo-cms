import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import AppShell from './AppShell.vue'
import { useAuthStore } from '../stores/authStore'
import { i18n } from '../i18n'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({ path: '/collections/article' }),
  RouterView: { template: '<div/>' },
}))

const mountShell = () =>
  mount(AppShell, { global: { plugins: [i18n], stubs: { RouterView: true, CollectionNav: true } } })

describe('AppShell', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-TW'
  })

  it('logout calls the store and routes to login', async () => {
    const store = useAuthStore()
    const logoutSpy = vi.spyOn(store, 'logout').mockResolvedValue()
    const wrapper = mountShell()
    await wrapper.find('button.logout').trigger('click')
    await new Promise((r) => setTimeout(r, 0))
    expect(logoutSpy).toHaveBeenCalledOnce()
    expect(push).toHaveBeenCalledWith({ name: 'login' })
  })

  it('renders the logout label via i18n and reacts to locale', async () => {
    const wrapper = mountShell()
    expect(wrapper.find('button.logout').text()).toContain('登出')
    i18n.global.locale.value = 'en'
    await wrapper.vm.$nextTick()
    expect(wrapper.find('button.logout').text()).toContain('Log out')
  })
})
