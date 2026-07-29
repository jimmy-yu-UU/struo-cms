import { test as base, expect } from '@playwright/test'

// The SPA's UI locale defaults to zh-TW
// (resolveInitialUiLocale reads ONLY localStorage['struo.uiLocale'], no navigator fallback). A
// fresh Playwright context starts with empty localStorage, so the app boots in Traditional
// Chinese and every i18n-string selector across the whole suite (Save, History, Search, Unsaved
// changes, etc.) fails to match. Seed the locale via an init script on the shared `page` fixture
// so every spec gets it automatically instead of repeating the workaround per-file.
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
