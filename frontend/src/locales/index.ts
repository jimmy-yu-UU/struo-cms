import zhTW from './zh-TW'
import en from './en'

// The one registry every UI-locale decision derives from: the UiLocale type, the i18n messages,
// the switcher options and the key-symmetry test. Adding a catalog is adding a file and a line here,
// plus a `lang.<code>` label in every catalog; which catalogs are *offered* is AdminUi:Locales.
export const catalogs = { 'zh-TW': zhTW, en } as const

export type UiLocale = keyof typeof catalogs

export const CATALOG_LOCALES = Object.keys(catalogs) as readonly UiLocale[]

export function isUiLocale(value: unknown): value is UiLocale {
  return typeof value === 'string' && Object.hasOwn(catalogs, value)
}
