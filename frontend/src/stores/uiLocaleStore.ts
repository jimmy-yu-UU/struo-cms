import { defineStore } from 'pinia'
import { i18n } from '../i18n'
import { CATALOG_LOCALES, type UiLocale } from '../locales'
import { readSavedUiLocale, resolveInitialUiLocale, UI_LOCALE_STORAGE_KEY } from '../theme/resolveInitialUiLocale'

export const useUiLocaleStore = defineStore('uiLocale', {
  state: () => ({
    locale: CATALOG_LOCALES[0] as UiLocale,
    enabled: [...CATALOG_LOCALES] as UiLocale[],
  }),
  actions: {
    // Called once after /api/config resolves (or fails), before the app mounts: fixes the enabled
    // set, moves the i18n fallback onto the configured default, and applies the effective locale.
    configure(enabled: readonly UiLocale[], defaultLocale: UiLocale): void {
      this.enabled = [...enabled]
      i18n.global.fallbackLocale.value = defaultLocale
      this.apply(resolveInitialUiLocale(readSavedUiLocale(), enabled, defaultLocale))
    },
    // Returns false and changes nothing for a locale outside the enabled set.
    set(locale: UiLocale): boolean {
      if (!this.enabled.includes(locale)) return false
      this.apply(locale)
      try {
        localStorage.setItem(UI_LOCALE_STORAGE_KEY, locale)
      } catch {
        /* localStorage unavailable — locale is still applied in-memory */
      }
      return true
    },
    apply(locale: UiLocale): void {
      this.locale = locale
      i18n.global.locale.value = locale
      document.documentElement.setAttribute('lang', locale)
    },
  },
})
