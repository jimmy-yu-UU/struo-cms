import type { FieldMeta } from '../types/schema'

/**
 * FE-R5 translation-completeness dot semantics (option a): a locale "has content"
 * when at least one of its translatable fields holds a non-empty value. Pure —
 * computed from the in-memory form model, no backend aggregate. NOT a validity or
 * required-field check.
 */
function isNonEmpty(value: unknown): boolean {
  if (value === undefined || value === null) return false
  if (typeof value === 'string') return value.trim().length > 0
  if (Array.isArray(value)) return value.length > 0
  return true // numbers (incl. 0), booleans (incl. false), objects count as content
}

export function hasLocaleContent(fields: FieldMeta[], values: Record<string, unknown>): boolean {
  return fields.some((f) => isNonEmpty(values[f.name]))
}
