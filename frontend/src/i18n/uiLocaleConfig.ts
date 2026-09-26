import { CATALOG_LOCALES, isUiLocale, type UiLocale } from '../locales'

export type UiLocaleConfig = { enabled: UiLocale[]; defaultLocale: UiLocale }

// The server validates the shape of AdminUi:Locales but cannot know which catalogs this build ships;
// that check lives here. Both fallbacks below only fire on a misconfigured server.
export function applyUiLocaleConfig(uiLocales: readonly string[], uiDefaultLocale: string): UiLocaleConfig {
  const unknown = uiLocales.filter((l) => !isUiLocale(l))
  let enabled = uiLocales.filter(isUiLocale)
  if (unknown.length > 0) {
    console.error(`AdminUi:Locales names catalogs this build does not ship: ${unknown.join(', ')}`)
  }
  if (enabled.length === 0) {
    enabled = [...CATALOG_LOCALES]
  }
  const defaultLocale = isUiLocale(uiDefaultLocale) && enabled.includes(uiDefaultLocale) ? uiDefaultLocale : enabled[0]
  return { enabled, defaultLocale }
}
