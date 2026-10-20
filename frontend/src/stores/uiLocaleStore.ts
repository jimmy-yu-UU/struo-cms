import { defineStore } from 'pinia'
import { i18n } from '../i18n'
import { DEFAULT_UI_LOCALE } from '../i18n/uiLocaleConfig'
import { CATALOG_LOCALES, type UiLocale } from '../locales'
import { readSavedUiLocale, resolveInitialUiLocale, UI_LOCALE_STORAGE_KEY } from '../theme/resolveInitialUiLocale'

export const useUiLocaleStore = defineStore('uiLocale', {
  state: () => ({
    locale: DEFAULT_UI_LOCALE as UiLocale,
    enabled: [...CATALOG_LOCALES] as UiLocale[],
  }),
  actions: {
    // Called once before the app mounts: a saved choice wins, otherwise the build's default applies.
    init(): void {
      this.apply(resolveInitialUiLocale(readSavedUiLocale(), this.enabled, DEFAULT_UI_LOCALE))
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
