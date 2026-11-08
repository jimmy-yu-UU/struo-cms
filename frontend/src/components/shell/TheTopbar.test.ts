import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { h } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import { SidebarProvider } from '@/components/ui/sidebar'
import TheTopbar from './TheTopbar.vue'

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ name: 'dashboard', params: {} }),
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountTopbar() {
  return mount(SidebarProvider, {
    slots: { default: () => h(TheTopbar) },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

describe('TheTopbar', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('renders a sidebar trigger as its leading control', () => {
    const w = mountTopbar()
    const first = w.findAll('button')[0]
    expect(first.attributes('data-sidebar')).toBe('trigger')
  })

  // The vendored SidebarTrigger only ships a hardcoded English "Toggle Sidebar" sr-only span, so
  // the sidebar trigger needs a localized accessible name via shell.openMenu, for zh-TW
  // screen-reader users.
  it('carries a localized accessible name on the sidebar trigger', () => {
    const w = mountTopbar()
    const first = w.findAll('button')[0]
    expect(first.attributes('aria-label')).toBe(en.shell.openMenu)
  })

  // mountTopbar renders the real subcomponents, so assert on each control's own stable
  // aria-label rather than a stub marker class.
  it('mounts the three topbar controls', () => {
    const w = mountTopbar()
    expect(w.find(`[aria-label="${en.lang.label}"]`).exists()).toBe(true)
    expect(w.find(`[aria-label="${en.theme.toggle}"]`).exists()).toBe(true)
    expect(w.find(`[aria-label="${en.user.account}"]`).exists()).toBe(true)
  })
})

describe('TheTopbar under zh-TW', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('gives the sidebar trigger a Chinese screen-reader label', async () => {
    const { default: zhTW } = await import('@/locales/zh-TW')
    const zh = createI18n({ legacy: false, locale: 'zh-TW', messages: { 'zh-TW': zhTW } })
    const w = mount(SidebarProvider, {
      slots: { default: () => h(TheTopbar) },
      global: { plugins: [zh], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    const trigger = w.findAll('button')[0]
    expect(trigger.get('.sr-only').text()).toBe('展開／收合側欄')
  })
})
