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

  it('has zh-TW strings for every new key', () => {
    for (const key of ['serverUnreachable', 'connectionTitle', 'connectionBody'] as const) {
      expect(zhTW.errors[key]).toBeTruthy()
      expect(en.errors[key]).toBeTruthy()
    }
  })
})
