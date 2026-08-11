import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import zhTW from '../locales/zh-TW'
import en from '../locales/en'

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en, 'zh-TW': zhTW } })

async function mountView() {
  const DashboardView = (await import('./DashboardView.vue')).default
  return mount(DashboardView, { global: { plugins: [i18n] } })
}

describe('DashboardView', () => {
  it('renders the dashboard heading and a placeholder line', async () => {
    const w = await mountView()
    expect(w.get('h1').text()).toBe('Dashboard')
    expect(w.text()).toContain('A more useful dashboard is coming in a future release.')
  })
})
