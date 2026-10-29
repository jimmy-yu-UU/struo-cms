import type { FieldMeta } from '../types/schema'
import { isEmpty } from './fieldTypes/types'

/**
 * Error-map key for a translatable field. The default locale keeps the bare field name (the shape
 * every shared field and every locale-less server detail uses); any other locale is `locale:field`.
 */
export function translationErrorKey(locale: string, field: string, defaultCode: string): string {
  return locale === defaultCode ? field : `${locale}:${field}`
}

/** True when `errors` holds at least one translatable-field error for `locale`. */
export function localeHasErrors(
  errors: Record<string, string>,
  locale: string,
  translatable: FieldMeta[],
  defaultCode: string,
): boolean {
  return translatable.some((f) => Object.hasOwn(errors, translationErrorKey(locale, f.name, defaultCode)))
}

/**
 * The predicate `buildItemPayload` uses to decide whether a locale's translation is sent: at least one
 * translatable field is non-empty. Validation judges exactly the locales that would be sent.
 */
export function localeIsSent(translatable: FieldMeta[], values: Record<string, unknown>): boolean {
  return translatable.some((f) => !isEmpty(values[f.name]))
}
