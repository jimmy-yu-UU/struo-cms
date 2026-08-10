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

function mountSidebar() {
  return mount(SidebarProvider, {
    slots: { default: () => h(TheSidebar) },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

describe('TheSidebar', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockClear()
    const auth = useAuthStore()
    auth.user = { id: '1', email: 'a@b.c', isSuperAdmin: true, permissions: {} } as never
    const schema = useSchemaStore()
    schema.collections = [
      { name: 'article', label: 'Articles', group: 'Content', icon: 'article', hidden: false, fields: [] },
      { name: 'page', label: 'Pages', group: '', icon: null, hidden: false, fields: [] },
    ] as never
    schema.loadError = ''
  })

  it('renders the pinned system entries', () => {
    const text = mountSidebar().text()
    expect(text).toContain(en.nav.dashboard)
    expect(text).toContain(en.nav.media)
    expect(text).toContain(en.nav.settings)
  })

  it('renders ungrouped collections at the top level and grouped ones under their group', () => {
    const text = mountSidebar().text()
    expect(text).toContain('Pages')
    expect(text).toContain('Content')
    expect(text).toContain('Articles')
  })

  it('navigates to a collection when its item is activated', async () => {
    const w = mountSidebar()
    const button = w.findAll('button').find((b) => b.text().includes('Pages'))
    expect(button).toBeDefined()
    await button!.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'page' } })
  })

  it('shows a retry affordance when the schema failed to load', () => {
    useSchemaStore().loadError = 'boom'
    const w = mountSidebar()
    expect(w.text()).toContain('boom')
    expect(w.text()).toContain(en.common.retry)
  })
})
