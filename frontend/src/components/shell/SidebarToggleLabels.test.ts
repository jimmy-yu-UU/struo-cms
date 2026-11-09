import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { h } from 'vue'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import zhTW from '@/locales/zh-TW'
import { SidebarProvider, SidebarRail, SidebarTrigger } from '@/components/ui/sidebar'

const i18n = createI18n({ legacy: false, locale: 'zh-TW', fallbackLocale: 'en', messages: { en, 'zh-TW': zhTW } })

function mountIn(child: () => ReturnType<typeof h>) {
  return mount(SidebarProvider, { slots: { default: child }, global: { plugins: [i18n] } })
}

describe('vendored sidebar toggles take their label from the caller', () => {
  it('renders the English name on the rail and the trigger when the English text is passed', () => {
    const rail = mountIn(() => h(SidebarRail, { label: en.shell.toggleSidebar })).get('[data-slot="sidebar-rail"]')
    expect(rail.attributes('aria-label')).toBe(en.shell.toggleSidebar)
    expect(rail.attributes('title')).toBe(en.shell.toggleSidebar)
    expect(mountIn(() => h(SidebarTrigger, { label: en.shell.toggleSidebar })).get('.sr-only').text()).toBe(en.shell.toggleSidebar)
  })

  it('renders the Chinese name on the rail', () => {
    expect(zhTW.shell.toggleSidebar).toBe('展開／收合側欄')
    const rail = mountIn(() => h(SidebarRail, { label: zhTW.shell.toggleSidebar })).get('[data-slot="sidebar-rail"]')
    expect(rail.attributes('aria-label')).toBe('展開／收合側欄')
    expect(rail.attributes('title')).toBe('展開／收合側欄')
  })

  it('renders the Chinese name on the trigger', () => {
    const trigger = mountIn(() => h(SidebarTrigger, { label: zhTW.shell.toggleSidebar }))
    expect(trigger.get('.sr-only').text()).toBe('展開／收合側欄')
  })
})
