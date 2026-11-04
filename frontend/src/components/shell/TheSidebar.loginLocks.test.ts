import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { h } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import { SidebarProvider } from '@/components/ui/sidebar'
import TheSidebar from './TheSidebar.vue'
import { useAuthStore } from '@/stores/authStore'
import { useSchemaStore } from '@/stores/schemaStore'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({ name: 'dashboard', params: {} }),
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function sidebarText(user: { isSuperAdmin: boolean; capabilities?: string[] }): string {
  useAuthStore().user = { id: '1', email: 'a@b.c', permissions: {}, ...user }
  return mount(SidebarProvider, {
    slots: { default: () => h(TheSidebar) },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  }).text()
}

describe('TheSidebar login locks entry', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useSchemaStore().collections = []
  })

  it('shows for a super-admin', () => {
    expect(sidebarText({ isSuperAdmin: true })).toContain(en.nav.loginLocks)
  })

  it('shows for a non-admin holding login.unlock, without Settings', () => {
    const text = sidebarText({ isSuperAdmin: false, capabilities: ['login.unlock'] })
    expect(text).toContain(en.nav.loginLocks)
    expect(text).not.toContain(en.nav.settings)
  })

  it('is hidden for a user without the capability', () => {
    expect(sidebarText({ isSuperAdmin: false, capabilities: [] })).not.toContain(en.nav.loginLocks)
  })
})
