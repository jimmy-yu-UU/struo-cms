import type { ValidationDetail } from '../api/apiClient'
import { translationErrorKey } from './translationErrors'

export type ServerErrorLocales = {
  defaultLocale: string
  locales: ReadonlySet<string>
}

/**
 * Splits server-side validation `error.details` into per-field errors and
 * leftovers. A detail whose `field` matches a known field (case-insensitive —
 * ASP.NET ModelState keys do not preserve declared casing) is written to
 * `fieldErrors` under the meta's canonical name; everything else falls through
 * to `leftover` for the form's error banner.
 *
 * A detail carrying a `locale` is keyed by that locale's translation error key (see
 * `translationErrorKey`); a locale that is not in `localeInfo.locales` falls through to `leftover`.
 * Without `localeInfo` a locale on a detail is ignored.
 *
 * Only the first message per key is kept, matching `validateItem`'s
 * one-message-per-field convention.
 */
export function splitServerErrors(
  details: ValidationDetail[] | undefined,
  knownFields: ReadonlySet<string>,
  localeInfo?: ServerErrorLocales,
): { fieldErrors: Record<string, string>; leftover: string[] } {
  const fieldErrors: Record<string, string> = {}
  const leftover: string[] = []
  if (!details || details.length === 0) return { fieldErrors, leftover }

  const canonicalByLower = new Map<string, string>()
  for (const name of knownFields) canonicalByLower.set(name.toLowerCase(), name)
  const localeByLower = new Map<string, string>()
  for (const code of localeInfo?.locales ?? []) localeByLower.set(code.toLowerCase(), code)

  for (const d of details) {
    const canonical = canonicalByLower.get((d.field ?? '').toLowerCase())
    const locale = d.locale && localeInfo ? localeByLower.get(d.locale.toLowerCase()) : undefined
    const unknownLocale = Boolean(d.locale && localeInfo && locale === undefined)
    if (canonical === undefined || unknownLocale) {
      leftover.push(d.message)
      continue
    }
    const key = locale !== undefined && localeInfo
      ? translationErrorKey(locale, canonical, localeInfo.defaultLocale)
      : canonical
    if (!Object.hasOwn(fieldErrors, key)) fieldErrors[key] = d.message
  }
  return { fieldErrors, leftover }
}
