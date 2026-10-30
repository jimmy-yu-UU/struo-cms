import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import LoginView from './LoginView.vue'
import { useAppConfigStore } from '../stores/appConfigStore'
import { i18n } from '../i18n'

const { push, replace, route } = vi.hoisted(() => ({
  push: vi.fn(),
  replace: vi.fn(),
  route: { query: {} as Record<string, unknown> },
}))
vi.mock('vue-router', () => ({ useRouter: () => ({ push, replace }), useRoute: () => route }))
vi.mock('../api/apiClient', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/apiClient')>()),
  apiBaseUrl: 'https://api.example.com/api',
}))

const MS = { id: 'microsoft', displayName: 'Microsoft 365', icon: 'microsoft' }
const ORIGIN = 'https://admin.example.com'

async function clickSso(): Promise<string> {
  const original = window.location
  Object.defineProperty(window, 'location', { configurable: true, value: { href: '', origin: ORIGIN } })
  try {
    const wrapper = mount(LoginView, { global: { plugins: [i18n] } })
    await wrapper.find('[data-test="sso"]').trigger('click')
    return window.location.href
  } finally {
    Object.defineProperty(window, 'location', { configurable: true, value: original })
  }
}

describe('LoginView SSO with a cross-origin apiBaseUrl', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    route.query = {}
    useAppConfigStore().oidcProviders = [MS]
    i18n.global.locale.value = 'zh-TW'
  })

  it('sends the SPA origin plus the root path when there is no redirect', async () => {
    expect(await clickSso()).toBe(
      'https://api.example.com/api/auth/login/oidc/microsoft?returnUrl=https%3A//admin.example.com/',
    )
  })

  it('composes origin and the validated path, keeping its query', async () => {
    route.query = { redirect: '/collections/article?page=2' }
    expect(await clickSso()).toBe(
      'https://api.example.com/api/auth/login/oidc/microsoft?returnUrl=https%3A//admin.example.com/collections/article%3Fpage%3D2',
    )
  })

  it.each(['//evil.example', 'https://evil.example/x'])('never forwards the unsafe redirect %s', async (bad) => {
    route.query = { redirect: bad }
    const href = await clickSso()
    expect(href).toBe('https://api.example.com/api/auth/login/oidc/microsoft?returnUrl=https%3A//admin.example.com/')
    expect(href).not.toContain('evil')
  })

  it('shows the SSO error code from the SPA /login?error= landing', async () => {
    route.query = { error: 'external_login_denied' }
    const wrapper = mount(LoginView, { global: { plugins: [i18n] } })
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[role="alert"]').text()).toContain('拒絕')
    expect(replace).toHaveBeenCalledWith({ name: 'login' })
  })
})
