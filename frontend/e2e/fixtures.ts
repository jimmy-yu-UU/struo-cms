import { test as base, expect } from '@playwright/test'

// The SPA's UI locale is the saved localStorage['struo.uiLocale'] when it names a registered
// catalog, otherwise the build default (en unless VITE_DEFAULT_UI_LOCALE overrides it). Seed the
// locale via an init script on the shared `page` fixture so every spec gets English i18n-string
// selectors (Save, History, Search, Unsaved changes, etc.) even against a build with another default.
//
// addInitScript persists for the page's lifetime and re-runs on every navigation/reload, so this
// single seed covers login's initial goto, page.reload(), and all in-test navigations.
export const test = base.extend({
  page: async ({ page }, use) => {
    await page.addInitScript(() => {
      try {
        localStorage.setItem('struo.uiLocale', 'en')
      } catch {
        /* ignore */
      }
    })
    await use(page)
  },
})

export { expect }
