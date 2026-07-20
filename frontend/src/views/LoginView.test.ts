import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PrimeVue from 'primevue/config'
import LoginView from './LoginView.vue'
import { useAuthStore } from '../stores/authStore'
import { useAppConfigStore } from '../stores/appConfigStore'
import { i18n } from '../i18n'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

const mountLogin = () => mount(LoginView, { global: { plugins: [i18n, PrimeVue] } })

describe('LoginView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-TW'
  })

  it('renders the configured brand name', () => {
    useAppConfigStore().brandName = 'Acme Docs'
    const wrapper = mountLogin()
    expect(wrapper.text()).toContain('Acme Docs')
  })

  it('calls authStore.login and navigates on success', async () => {
    const store = useAuthStore()
    const loginSpy = vi.spyOn(store, 'login').mockResolvedValue()
    const wrapper = mountLogin()
    await wrapper.find('input[type="email"]').setValue('a@b.com')
    await wrapper.find('input[type="password"]').setValue('pw')
    await wrapper.find('form').trigger('submit.prevent')
    await new Promise((r) => setTimeout(r, 0))
    expect(loginSpy).toHaveBeenCalledWith('a@b.com', 'pw')
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })
  })

  it('shows an error message when login fails', async () => {
    const store = useAuthStore()
    vi.spyOn(store, 'login').mockRejectedValue(new Error('Invalid credentials.'))
    const wrapper = mountLogin()
    await wrapper.find('input[type="email"]').setValue('a@b.com')
    await wrapper.find('input[type="password"]').setValue('bad')
    await wrapper.find('form').trigger('submit.prevent')
    await new Promise((r) => setTimeout(r, 0))
    expect(wrapper.text()).toContain('Invalid credentials.')
  })

  it('hides the SSO button when oidc is disabled', () => {
    useAppConfigStore().oidcEnabled = false
    const wrapper = mountLogin()
    expect(wrapper.find('[data-test="sso"]').exists()).toBe(false)
  })

  it('shows the SSO button and navigates on click when oidc is enabled', async () => {
    useAppConfigStore().oidcEnabled = true
    const original = window.location
    Object.defineProperty(window, 'location', { configurable: true, value: { href: '' } })
    try {
      const wrapper = mountLogin()
      const sso = wrapper.find('[data-test="sso"]')
      expect(sso.exists()).toBe(true)
      await sso.trigger('click')
      expect(window.location.href).toBe('/api/auth/login/oidc?returnUrl=/')
    } finally {
      Object.defineProperty(window, 'location', { configurable: true, value: original })
    }
  })
})
