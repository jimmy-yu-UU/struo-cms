import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '../locales/en'
import LoginLocksView from './LoginLocksView.vue'
import { useAuthStore } from '../stores/authStore'
import { loginLocksApi } from '../api/loginLocksApi'

vi.mock('../api/loginLocksApi', () => ({
  loginLocksApi: { get: vi.fn(), clear: vi.fn() },
}))
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: vi.fn() }) }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function signIn(user: { isSuperAdmin: boolean; capabilities?: string[] }) {
  useAuthStore().user = { id: '1', permissions: {}, ...user }
}
function mountView() {
  return mount(LoginLocksView, { global: { plugins: [i18n] } })
}
async function lookUp(w: ReturnType<typeof mountView>, email: string) {
  await w.get('input[type="email"]').setValue(email)
  await w.get('form').trigger('submit')
  await flushPromises()
}

describe('LoginLocksView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.mocked(loginLocksApi.get).mockResolvedValue({ locked: true, retryAfterSeconds: 90 })
    vi.mocked(loginLocksApi.clear).mockResolvedValue(undefined)
  })

  it('shows the denial instead of the form without the capability', () => {
    signIn({ isSuperAdmin: false, capabilities: [] })
    const w = mountView()
    expect(w.text()).toContain(en.loginLocks.notPermitted)
    expect(w.find('form').exists()).toBe(false)
  })

  it.each([
    ['a super-admin', { isSuperAdmin: true }],
    ['a holder of login.unlock', { isSuperAdmin: false, capabilities: ['login.unlock'] }],
  ])('shows the lookup form to %s', (_name, user) => {
    signIn(user)
    expect(mountView().find('form').exists()).toBe(true)
  })

  it('does not query until an email is submitted, and disables Look up while blank', async () => {
    signIn({ isSuperAdmin: true })
    const w = mountView()
    await flushPromises()
    expect(loginLocksApi.get).not.toHaveBeenCalled()
    expect(w.get('button[type="submit"]').attributes('disabled')).toBeDefined()
  })

  it('looks up the trimmed email, shows the lock and unlocks it', async () => {
    signIn({ isSuperAdmin: true })
    const w = mountView()
    await lookUp(w, '  a@x.test ')
    expect(loginLocksApi.get).toHaveBeenCalledWith('a@x.test')
    expect(w.text()).toContain('2 min')

    vi.mocked(loginLocksApi.get).mockResolvedValue({ locked: false, retryAfterSeconds: 0 })
    await w.findAll('button').find((b) => b.text() === en.loginLocks.unlock)!.trigger('click')
    await flushPromises()
    expect(loginLocksApi.clear).toHaveBeenCalledWith('a@x.test')
    expect(w.text()).toContain(en.loginLocks.notLocked)
  })

  it('queries again when the same email is submitted twice', async () => {
    signIn({ isSuperAdmin: true })
    const w = mountView()
    await lookUp(w, 'a@x.test')
    await w.get('form').trigger('submit')
    await flushPromises()
    expect(loginLocksApi.get).toHaveBeenCalledTimes(2)
  })
})
