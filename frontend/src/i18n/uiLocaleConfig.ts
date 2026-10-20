import { CATALOG_LOCALES, type UiLocale } from '../locales'

export const FALLBACK_UI_LOCALE: UiLocale = 'en'

// A fork overrides the default at build time with VITE_DEFAULT_UI_LOCALE. A value that names no
// registered catalog logs a warning and falls back to `en`, so the UI never boots without messages.
export function resolveDefaultUiLocale(raw: string | undefined): UiLocale {
  const requested = raw?.trim()
  if (!requested) return FALLBACK_UI_LOCALE
  const match = CATALOG_LOCALES.find((c) => c.toLowerCase() === requested.toLowerCase())
  if (match !== undefined) return match
  console.warn(
    `VITE_DEFAULT_UI_LOCALE="${requested}" is not a registered catalog (${CATALOG_LOCALES.join(', ')}); using ${FALLBACK_UI_LOCALE}.`,
  )
  return FALLBACK_UI_LOCALE
}

export const DEFAULT_UI_LOCALE: UiLocale = resolveDefaultUiLocale(import.meta.env.VITE_DEFAULT_UI_LOCALE)
