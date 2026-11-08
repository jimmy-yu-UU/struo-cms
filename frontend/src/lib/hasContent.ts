import type { FieldMeta } from '../types/schema'

// Mirrors the backend's RichTextCleaner.IsBlankHtml: markup is blank when it has no image or rule and
// nothing but whitespace (non-breaking spaces included) is left once the tags are removed.
const MEDIA_TAG = /<(?:img|hr)/i
const ANY_TAG = /<[^<>]*>/g
const NBSP = /&nbsp;/gi

/** True for a string that is empty, whitespace, or rich text markup carrying no content. */
export function isBlankMarkup(value: string): boolean {
  if (MEDIA_TAG.test(value)) return false
  return value.replace(ANY_TAG, '').replace(NBSP, ' ').trim().length === 0
}

/**
 * The single "does this value have content" predicate for translations: it decides which locales are
 * sent, validated, shown as complete and cleared. Numbers (incl. 0) and booleans (incl. false) are
 * content; null, blank text and empty collections are not, and neither is empty markup in a rich text
 * field (plain text fields are judged by trimming only, so a value such as "<TBD>" is content).
 * Required-field checks use `isEmpty`, which applies the same per-field rule.
 */
export function hasContent(value: unknown, field?: { interface: string }): boolean {
  if (value === undefined || value === null) return false
  if (typeof value === 'string') return field?.interface === 'richText' ? !isBlankMarkup(value) : value.trim().length > 0
  if (Array.isArray(value)) return value.length > 0
  if (typeof value === 'object') return Object.keys(value).length > 0
  return true
}

export function anyHasContent(fields: FieldMeta[], values: Record<string, unknown>): boolean {
  return fields.some((f) => hasContent(values[f.name], f))
}
