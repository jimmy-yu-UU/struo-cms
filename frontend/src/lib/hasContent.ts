import type { FieldMeta } from '../types/schema'

// Rich text markup that carries no content: paragraph tags and line breaks only.
const EMPTY_MARKUP = /<p(?:\s[^>]*)?>|<\/p>|<br\s*\/?>/gi

/** True for a string that is empty, whitespace, or rich text markup carrying no content. */
export function isBlankMarkup(value: string): boolean {
  return value.replace(EMPTY_MARKUP, '').trim().length === 0
}

/**
 * The single "does this value have content" predicate for translations: it decides which locales are
 * sent, validated, shown as complete and cleared. Numbers (incl. 0) and booleans (incl. false) are
 * content; null, blank text, empty collections and empty rich text markup are not. Required-field
 * checks use `isEmpty`, which only treats markup as empty for a rich text field.
 */
export function hasContent(value: unknown): boolean {
  if (value === undefined || value === null) return false
  if (typeof value === 'string') return !isBlankMarkup(value)
  if (Array.isArray(value)) return value.length > 0
  if (typeof value === 'object') return Object.keys(value).length > 0
  return true
}

export function anyHasContent(fields: FieldMeta[], values: Record<string, unknown>): boolean {
  return fields.some((f) => hasContent(values[f.name]))
}
