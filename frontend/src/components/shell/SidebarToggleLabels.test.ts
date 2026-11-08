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
  it('keeps the English default when no label is passed', () => {
    const rail = mountIn(() => h(SidebarRail)).get('[data-slot="sidebar-rail"]')
    expect(rail.attributes('aria-label')).toBe('Toggle Sidebar')
    expect(rail.attributes('title')).toBe('Toggle Sidebar')
    expect(mountIn(() => h(SidebarTrigger)).get('.sr-only').text()).toBe('Toggle Sidebar')
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

  it('has an English locale string too', () => {
    expect(en.shell.toggleSidebar).toBe('Toggle sidebar')
  })
})
