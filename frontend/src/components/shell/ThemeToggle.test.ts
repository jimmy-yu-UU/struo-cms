import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ThemeToggle from './ThemeToggle.vue'
import { useThemeStore } from '../../stores/themeStore'
import { i18n } from '../../i18n'

describe('ThemeToggle', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    document.documentElement.classList.remove('app-dark')
    i18n.global.locale.value = 'zh-TW'
  })

  it('shows the moon icon in light mode and toggles to dark on click', async () => {
    const store = useThemeStore()
    store.set('light')
    const wrapper = mount(ThemeToggle, { global: { plugins: [i18n] } })
    expect(wrapper.find('.pi-moon').exists()).toBe(true)
    await wrapper.find('button.theme-toggle').trigger('click')
    expect(store.isDark).toBe(true)
    expect(wrapper.find('.pi-sun').exists()).toBe(true)
  })

  it('exposes an i18n aria-label', () => {
    const wrapper = mount(ThemeToggle, { global: { plugins: [i18n] } })
    expect(wrapper.find('button.theme-toggle').attributes('aria-label')).toBe('切換主題')
  })
})
