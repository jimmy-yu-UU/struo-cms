import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import LoginView from './LoginView.vue'
import { useAuthStore } from '../stores/authStore'
import { useAppConfigStore } from '../stores/appConfigStore'
import { i18n } from '../i18n'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

const mountLogin = () => mount(LoginView, { global: { plugins: [i18n] } })

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

  it('renders the vendored input and the shared PasswordInput', () => {
    const w = mountLogin()
    // Two native inputs, both from ui/input: the email field and PasswordInput's inner control.
    expect(w.findAll('[data-slot="input"]')).toHaveLength(2)
    expect(w.findComponent({ name: 'PasswordInput' }).exists()).toBe(true)
  })

  it('keeps the label associations and forwards id/autocomplete/required through PasswordInput', () => {
    const w = mountLogin()
    // PasswordInput has inheritAttrs: false and forwards $attrs to the inner input, so `id`,
    // `autocomplete` and `required` reach the native control and the <label for> keeps working.
    expect(w.find('input[type="email"]').attributes('id')).toBe('lg-email')
    const pw = w.find('input[type="password"]')
    expect(pw.attributes('id')).toBe('lg-pw')
    expect(pw.attributes('autocomplete')).toBe('current-password')
    expect(pw.attributes('required')).toBeDefined()
  })

  it('reveals and re-hides the password through the toggle', async () => {
    const w = mountLogin()
    const toggle = w.findComponent({ name: 'PasswordInput' }).get('button')
    expect(toggle.attributes('type')).toBe('button')
    await toggle.trigger('click')
    expect(w.find('input[type="text"]').exists()).toBe(true)
    await toggle.trigger('click')
    expect(w.find('input[type="password"]').exists()).toBe(true)
  })

  it('types every in-form button so only the submit button submits', () => {
    useAppConfigStore().oidcEnabled = true
    const w = mountLogin()
    const types = w.findAll('form button').map((b) => b.attributes('type'))
    // ui/button injects no type and a bare <button> defaults to type="submit"; exactly one control
    // in this form may submit it. This also guards the SSO button and the PasswordInput toggle,
    // both of which live inside this <form>: either one silently losing its type="button" would
    // push this count to two.
    expect(types.filter((t) => t === 'submit')).toHaveLength(1)
    expect(types.every((t) => t === 'submit' || t === 'button')).toBe(true)
  })

  it('swaps the submit label while the request is in flight, then swaps back', async () => {
    const store = useAuthStore()
    let release!: () => void
    vi.spyOn(store, 'login').mockReturnValue(new Promise<void>((r) => { release = r }))
    const w = mountLogin()
    const submitBtn = () => w.find('button[type="submit"]')
    expect(submitBtn().text()).toBe('登入')
    await w.find('form').trigger('submit.prevent')
    // ui/button has no loading prop, so the in-flight state is a label swap plus :disabled.
    expect(submitBtn().text()).toBe('登入中…')
    expect(submitBtn().attributes('disabled')).toBeDefined()
    release()
    await new Promise((r) => setTimeout(r, 0))
    expect(submitBtn().text()).toBe('登入')
    expect(submitBtn().attributes('disabled')).toBeUndefined()
  })
})
