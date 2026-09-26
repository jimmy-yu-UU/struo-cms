import { createI18n } from 'vue-i18n'
import { catalogs, CATALOG_LOCALES } from '../locales'

// Both locale and fallbackLocale start on the first bundled catalog. uiLocaleStore.configure() runs
// before the app mounts: it moves the fallback to the server's AdminUi default and the locale to the
// saved choice when that is enabled, otherwise to the same default.
export const i18n = createI18n({
  legacy: false,
  locale: CATALOG_LOCALES[0],
  fallbackLocale: CATALOG_LOCALES[0],
  messages: catalogs,
})
