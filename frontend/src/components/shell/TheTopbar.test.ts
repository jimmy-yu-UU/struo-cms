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

  // The vendored SidebarTrigger only ships a hardcoded English "Toggle Sidebar" sr-only
  // span — restores the localized accessible name the old hamburger carried via
  // shell.openMenu, for zh-TW screen-reader users.
  it('carries a localized accessible name on the sidebar trigger', () => {
    const w = mountTopbar()
    const first = w.findAll('button')[0]
    expect(first.attributes('aria-label')).toBe(en.shell.openMenu)
  })

  it('no longer renders the legacy drawer toggle', () => {
    expect(mountTopbar().find('.drawer-toggle').exists()).toBe(false)
  })

  // Old suite's "mounts the three topbar controls" test used stubs to assert presence;
  // the brief's mountTopbar renders the real subcomponents, so assert on each control's
  // own stable aria-label instead of a stub marker class.
  it('mounts the three topbar controls', () => {
    const w = mountTopbar()
    expect(w.find(`[aria-label="${en.lang.label}"]`).exists()).toBe(true)
    expect(w.find(`[aria-label="${en.theme.toggle}"]`).exists()).toBe(true)
    expect(w.find(`[aria-label="${en.user.account}"]`).exists()).toBe(true)
  })
})
