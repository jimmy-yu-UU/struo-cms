import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import AppShell from './AppShell.vue'
import { useAuthStore } from '../stores/authStore'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({ path: '/collections/article' }),
  RouterView: { template: '<div/>' },
}))

describe('AppShell', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('logout calls the store and routes to login', async () => {
    const store = useAuthStore()
    const logoutSpy = vi.spyOn(store, 'logout').mockResolvedValue()
    const wrapper = mount(AppShell, {
      global: { stubs: { RouterView: true, CollectionNav: true } },
    })
    await wrapper.find('button.logout').trigger('click')
    await new Promise((r) => setTimeout(r, 0))
    expect(logoutSpy).toHaveBeenCalledOnce()
    expect(push).toHaveBeenCalledWith({ name: 'login' })
  })
})
