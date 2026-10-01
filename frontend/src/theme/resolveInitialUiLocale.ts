import type { UiLocale } from '../locales'

export const UI_LOCALE_STORAGE_KEY = 'struo.uiLocale'

export function readSavedUiLocale(): string | null {
  try {
    return localStorage.getItem(UI_LOCALE_STORAGE_KEY)
  } catch {
    return null // localStorage unavailable
  }
}

// Pure: the saved value wins only when it is one of the enabled locales; otherwise the configured default.
export function resolveInitialUiLocale(
  saved: string | null,
  enabled: readonly UiLocale[],
  fallback: UiLocale,
): UiLocale {
  return saved !== null && (enabled as readonly string[]).includes(saved) ? (saved as UiLocale) : fallback
}
