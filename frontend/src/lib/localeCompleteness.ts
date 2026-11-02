import type { FieldMeta } from '../types/schema'
import { anyHasContent } from './hasContent'

/**
 * Translation-completeness dot semantics: a locale "has content"
 * when at least one of its translatable fields holds a value with content. Pure —
 * computed from the in-memory form model, no backend aggregate. NOT a validity or
 * required-field check.
 */
export function hasLocaleContent(fields: FieldMeta[], values: Record<string, unknown>): boolean {
  return anyHasContent(fields, values)
}
