import { defineStore } from 'pinia'
import { i18n } from '../i18n'
import { resolveInitialUiLocale, type UiLocale } from '../theme/resolveInitialUiLocale'

export const useUiLocaleStore = defineStore('uiLocale', {
  state: () => ({ locale: resolveInitialUiLocale() as UiLocale }),
  actions: {
    set(locale: UiLocale): void {
      this.locale = locale
      i18n.global.locale.value = locale
      document.documentElement.setAttribute('lang', locale)
      try {
        localStorage.setItem('struo.uiLocale', locale)
      } catch {
        /* localStorage unavailable — locale is still applied in-memory */
      }
    },
  },
})
