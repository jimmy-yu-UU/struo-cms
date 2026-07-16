import { createI18n } from 'vue-i18n'
import zhTW from '../locales/zh-TW'
import en from '../locales/en'
import { resolveInitialUiLocale } from '../theme/resolveInitialUiLocale'

export const i18n = createI18n({
  legacy: false,
  locale: resolveInitialUiLocale(),
  fallbackLocale: 'en',
  messages: { 'zh-TW': zhTW, en },
})
