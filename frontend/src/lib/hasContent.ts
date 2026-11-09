import type { FieldMeta } from '../types/schema'
import { isBlankMarkup } from './blankMarkup'
import { getFieldType } from './fieldTypes/registry'

/**
 * The single "does this value have content" predicate for translations: it decides which locales are
 * sent, validated, shown as complete and cleared. Numbers (incl. 0) and booleans (incl. false) are
 * content; null, blank text and empty collections are not, and neither is empty markup in a rich text
 * field (plain text fields are judged by trimming only, so a value such as "<TBD>" is content).
 * Required-field checks use `isEmpty`, which applies the same per-field rule.
 *
 * Without a field the judgement is structural (an array with elements is content). `anyHasContent`
 * judges each field by its serialized value, so a collection holding only blank elements (a blank
 * repeater row, a blank key-value key, an empty id) is no content, exactly as it is sent.
 */
export function hasContent(value: unknown, field?: { interface: string }): boolean {
  if (value === undefined || value === null) return false
  if (typeof value === 'string') return field?.interface === 'richText' ? !isBlankMarkup(value) : value.trim().length > 0
  if (Array.isArray(value)) return value.length > 0
  if (typeof value === 'object') return Object.keys(value).length > 0
  return true
}

export function anyHasContent(fields: FieldMeta[], values: Record<string, unknown>): boolean {
  return fields.some((f) => hasContent(getFieldType(f.interface).serialize(values[f.name], f), f))
}
