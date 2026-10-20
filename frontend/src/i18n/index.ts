import { createI18n } from 'vue-i18n'
import { catalogs } from '../locales'
import { DEFAULT_UI_LOCALE } from './uiLocaleConfig'

// Locale and fallback both start on the build's default; uiLocaleStore.init() moves the locale
// onto the user's saved choice before the app mounts.
export const i18n = createI18n({
  legacy: false,
  locale: DEFAULT_UI_LOCALE,
  fallbackLocale: DEFAULT_UI_LOCALE,
  messages: catalogs,
})
