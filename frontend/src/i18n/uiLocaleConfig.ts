import { CATALOG_LOCALES, type UiLocale } from '../locales'

export type UiLocaleConfig = { enabled: UiLocale[]; defaultLocale: UiLocale }

// The server validates the shape of AdminUi:Locales but cannot know which catalogs this build ships;
// that check lives here. Both fallbacks below only fire on a misconfigured server.
export function applyUiLocaleConfig(uiLocales: readonly string[], uiDefaultLocale: string): UiLocaleConfig {
  const toCatalog = (code: string): UiLocale | undefined =>
    CATALOG_LOCALES.find((c) => c.toLowerCase() === code.toLowerCase())
  const unknown = uiLocales.filter((l) => toCatalog(l) === undefined)
  let enabled = uiLocales.map(toCatalog).filter((c): c is UiLocale => c !== undefined)
  if (unknown.length > 0) {
    console.error(`AdminUi:Locales names catalogs this build does not ship: ${unknown.join(', ')}`)
  }
  if (enabled.length === 0) {
    enabled = [...CATALOG_LOCALES]
  }
  const configuredDefault = toCatalog(uiDefaultLocale)
  const defaultLocale =
    configuredDefault !== undefined && enabled.includes(configuredDefault) ? configuredDefault : enabled[0]
  return { enabled, defaultLocale }
}
