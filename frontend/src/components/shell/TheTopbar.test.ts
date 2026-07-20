import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TheTopbar from './TheTopbar.vue'
import { useSidebarStore } from '../../stores/sidebarStore'
import { i18n } from '../../i18n'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

const stubs = {
  UiLanguageSwitcher: { template: '<div class="stub-lang" />' },
  ThemeToggle: { template: '<div class="stub-theme" />' },
  UserMenu: { template: '<div class="stub-user" />' },
  BrandMark: { template: '<span class="stub-brandmark" />' },
}

describe('TheTopbar', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
    i18n.global.locale.value = 'zh-TW'
  })

  it('the hamburger toggles the drawer', async () => {
    const sidebar = useSidebarStore()
    const wrapper = mount(TheTopbar, { global: { plugins: [i18n], stubs } })
    await wrapper.find('button.drawer-toggle').trigger('click')
    expect(sidebar.drawerOpen).toBe(true)
  })

  it('the brand button routes to the dashboard', async () => {
    const wrapper = mount(TheTopbar, { global: { plugins: [i18n], stubs } })
    await wrapper.find('button.brand-btn').trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })
  })

  it('mounts the three topbar controls', () => {
    const wrapper = mount(TheTopbar, { global: { plugins: [i18n], stubs } })
    expect(wrapper.find('.stub-lang').exists()).toBe(true)
    expect(wrapper.find('.stub-theme').exists()).toBe(true)
    expect(wrapper.find('.stub-user').exists()).toBe(true)
  })

  it('renders the configured brand name', async () => {
    const { useAppConfigStore } = await import('../../stores/appConfigStore')
    useAppConfigStore().brandName = 'Acme Docs'
    const wrapper = mount(TheTopbar, { global: { plugins: [i18n], stubs } })
    expect(wrapper.find('.brand-btn').text()).toContain('Acme Docs')
  })
})
