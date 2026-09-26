import { createI18n } from 'vue-i18n'
import { catalogs, CATALOG_LOCALES } from '../locales'

// Both locale and fallbackLocale start on the first bundled catalog; uiLocaleStore.configure()
// moves them to the server's AdminUi default before the app mounts.
export const i18n = createI18n({
  legacy: false,
  locale: CATALOG_LOCALES[0],
  fallbackLocale: CATALOG_LOCALES[0],
  messages: catalogs,
})
