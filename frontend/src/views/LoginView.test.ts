import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import LoginView from './LoginView.vue'
import { useAuthStore } from '../stores/authStore'
import { useAppConfigStore } from '../stores/appConfigStore'
import { i18n } from '../i18n'
import { ApiError } from '../api/apiClient'

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
    // jsdom never applies <style scoped>, so this is the only guard on the subtitle's font-weight:
    // font-medium is what carries the 500 weight it needs. BrandMark also renders a <span> (its
    // logoless fallback initial) as an earlier sibling inside .brand, so the subtitle is the last.
    const brandSpans = wrapper.findAll('.brand span')
    expect(brandSpans.at(-1)?.classes()).toEqual(
      expect.arrayContaining(['text-xs', 'font-medium', 'text-muted-foreground']),
    )
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
    // A plain (non-ApiError) rejection is not one auth.login actually produces in practice — it
    // always throws ApiError, since it goes through apiClient — but it is still the fallback branch
    // of loginErrorMessage's instanceof check, so it must never surface the raw Error.message
    // (the server's prose is deliberately never displayed on this page any more).
    const store = useAuthStore()
    vi.spyOn(store, 'login').mockRejectedValue(new Error('Invalid credentials.'))
    const wrapper = mountLogin()
    await wrapper.find('input[type="email"]').setValue('a@b.com')
    await wrapper.find('input[type="password"]').setValue('bad')
    await wrapper.find('form').trigger('submit.prevent')
    await new Promise((r) => setTimeout(r, 0))
    expect(wrapper.text()).toContain('登入失敗,請稍後再試。') // t('login.failed'), the generic fallback
    expect(wrapper.text()).not.toContain('Invalid credentials.')
  })

  // ---- localized auth-failure mapping (machine-readable error.code, never the server's prose) ----

  it('shows a localized message for UNAUTHORIZED instead of the server prose', async () => {
    const store = useAuthStore()
    vi.spyOn(store, 'login').mockRejectedValue(new ApiError(401, 'Invalid credentials.', 'UNAUTHORIZED'))
    const wrapper = mountLogin()
    await wrapper.find('input[type="email"]').setValue('a@b.com')
    await wrapper.find('input[type="password"]').setValue('bad')
    await wrapper.find('form').trigger('submit.prevent')
    await new Promise((r) => setTimeout(r, 0))
    expect(wrapper.text()).toContain('Email 或密碼不正確。') // t('login.invalidCredentials')
    expect(wrapper.text()).not.toContain('Invalid credentials.') // never the server's raw prose
  })

  it('shows the deactivated-account message for ACCOUNT_INACTIVE', async () => {
    const store = useAuthStore()
    vi.spyOn(store, 'login').mockRejectedValue(new ApiError(401, 'Account is deactivated.', 'ACCOUNT_INACTIVE'))
    const wrapper = mountLogin()
    await wrapper.find('input[type="email"]').setValue('a@b.com')
    await wrapper.find('input[type="password"]').setValue('pw')
    await wrapper.find('form').trigger('submit.prevent')
    await new Promise((r) => setTimeout(r, 0))
    expect(wrapper.text()).toContain('此帳號已停用,請聯絡管理者。') // t('login.accountInactive')
    expect(wrapper.text()).not.toContain('Account is deactivated.')
  })

  it('shows the wait time for TOO_MANY_REQUESTS', async () => {
    const store = useAuthStore()
    vi.spyOn(store, 'login').mockRejectedValue(new ApiError(429, 'x', 'TOO_MANY_REQUESTS', undefined, 45))
    const wrapper = mountLogin()
    await wrapper.find('input[type="email"]').setValue('a@b.com')
    await wrapper.find('input[type="password"]').setValue('pw')
    await wrapper.find('form').trigger('submit.prevent')
    await new Promise((r) => setTimeout(r, 0))
    // t('errors.tooManyRequestsWithWait', { seconds: 45 }) — proves retryAfterSeconds reached the
    // message, not just the generic (no-wait) tooManyRequests text.
    expect(wrapper.text()).toContain('嘗試次數過多,請於 45 秒後再試。')
  })

  it('falls back to login.failed for an unrecognised failure', async () => {
    const store = useAuthStore()
    vi.spyOn(store, 'login').mockRejectedValue(new ApiError(500, 'Something broke.', 'INTERNAL_SERVER_ERROR'))
    const wrapper = mountLogin()
    await wrapper.find('input[type="email"]').setValue('a@b.com')
    await wrapper.find('input[type="password"]').setValue('pw')
    await wrapper.find('form').trigger('submit.prevent')
    await new Promise((r) => setTimeout(r, 0))
    expect(wrapper.text()).toContain('登入失敗,請稍後再試。') // t('login.failed')
    expect(wrapper.text()).not.toContain('Something broke.')
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
    // Identity guard: a hand-rolled <button> would satisfy a role- or text-based check just as
    // well, so the assertion targets data-slot="button" — ui/button's own hook, which its
    // underlying reka Primitive renders unconditionally. Counting those hooks is therefore blind
    // to the rendered text but not to the rendered element. Without OIDC there are two: the submit
    // button and the PasswordInput toggle.
    expect(w.findAll('[data-slot="button"]')).toHaveLength(2)
  })

  it('renders three data-slot="button" hooks when the SSO button also mounts', () => {
    useAppConfigStore().oidcEnabled = true
    const w = mountLogin()
    // submit + SSO + the PasswordInput toggle.
    expect(w.findAll('[data-slot="button"]')).toHaveLength(3)
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
    // .lucide-* is one of the two legal Tailwind-adjacent migration proofs in this project (the
    // other is data-slot); pin both icon identities, not just the functional type flip.
    expect(toggle.find('svg').classes()).toContain('lucide-eye')
    await toggle.trigger('click')
    expect(w.find('input[type="text"]').exists()).toBe(true)
    expect(toggle.find('svg').classes()).toContain('lucide-eye-off')
    await toggle.trigger('click')
    expect(w.find('input[type="password"]').exists()).toBe(true)
    expect(toggle.find('svg').classes()).toContain('lucide-eye')
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
