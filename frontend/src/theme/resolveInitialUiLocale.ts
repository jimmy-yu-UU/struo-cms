export type UiLocale = 'zh-TW' | 'en'

export const DEFAULT_UI_LOCALE: UiLocale = 'zh-TW'

export function resolveInitialUiLocale(): UiLocale {
  try {
    const saved = localStorage.getItem('struo.uiLocale')
    if (saved === 'zh-TW' || saved === 'en') return saved
  } catch {
    /* localStorage unavailable — fall through to the default */
  }
  return DEFAULT_UI_LOCALE
}
