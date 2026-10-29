import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ConnectionErrorScreen from './ConnectionErrorScreen.vue'
import en from '@/locales/en'
import zhTW from '@/locales/zh-TW'

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

describe('ConnectionErrorScreen', () => {
  it('shows the unreachable message and a Retry button that emits retry', async () => {
    const w = mount(ConnectionErrorScreen, { global: { plugins: [i18n] } })
    expect(w.text()).toContain(en.errors.connectionTitle)
    expect(w.text()).toContain(en.common.retry)
    await w.get('button').trigger('click')
    expect(w.emitted('retry')).toHaveLength(1)
  })

  it('disables Retry and shows the retrying label while busy', () => {
    const w = mount(ConnectionErrorScreen, { props: { busy: true }, global: { plugins: [i18n] } })
    const button = w.get('button')
    expect(button.attributes('disabled')).toBeDefined()
    expect(button.text()).toBe(en.errors.connectionRetrying)
  })

  it('moves focus to the heading on mount and again after a failed retry', async () => {
    const w = mount(ConnectionErrorScreen, { attachTo: document.body, global: { plugins: [i18n] } })
    const heading = w.get('h1').element
    expect(document.activeElement).toBe(heading)
    ;(document.activeElement as HTMLElement).blur()
    await w.setProps({ busy: true })
    await w.setProps({ busy: false })
    expect(document.activeElement).toBe(heading)
    w.unmount()
  })

  it('has zh-TW strings for every new key', () => {
    for (const key of ['serverUnreachable', 'connectionTitle', 'connectionBody', 'connectionRetrying'] as const) {
      expect(zhTW.errors[key]).toBeTruthy()
      expect(en.errors[key]).toBeTruthy()
    }
  })
})
