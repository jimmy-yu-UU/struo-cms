import type { Component } from 'vue'
import type { FieldMeta } from '../../types/schema'
import { isBlankMarkup } from '../hasContent'

// Mirrors backend Struo.Domain.Metadata.Enums.FieldInterface (camelCase).
// Adding a backend interface requires adding it here AND giving it a component in registry.ts.
// Enforced, not honour-system: frontend/tests/schemaContract.test.ts checks this two ways — one
// describe block fails when an interface used by a core collection is missing from this list or
// from the registry; a second, unconditional block fails when any backend-declared interface is
// missing, whether or not a core collection uses it (see schema/README.md).
export type FieldInterface =
  | 'text' | 'textarea' | 'richText' | 'markdown' | 'code'
  | 'slug' | 'email' | 'url' | 'password' | 'color' | 'phone'
  | 'number' | 'slider' | 'rating'
  | 'boolean' | 'checkbox'
  | 'date' | 'time' | 'dateTime'
  | 'select' | 'multiSelect' | 'radio' | 'checkboxGroup' | 'tags'
  | 'json' | 'keyValue' | 'repeater'
  | 'file' | 'image' | 'files'
  | 'hidden' | 'divider' | 'uuid'

export const ALL_FIELD_INTERFACES: readonly FieldInterface[] = [
  'text', 'textarea', 'richText', 'markdown', 'code',
  'slug', 'email', 'url', 'password', 'color', 'phone',
  'number', 'slider', 'rating',
  'boolean', 'checkbox',
  'date', 'time', 'dateTime',
  'select', 'multiSelect', 'radio', 'checkboxGroup', 'tags',
  'json', 'keyValue', 'repeater',
  'file', 'image', 'files',
  'hidden', 'divider', 'uuid',
]

export interface FieldTypeDef {
  component: Component
  defaultValue(field: FieldMeta): unknown
  parse(raw: unknown, field: FieldMeta): unknown
  serialize(value: unknown, field: FieldMeta): unknown
  listColumn: { format(value: unknown, field: FieldMeta): string } | null
  validate?(value: unknown, field: FieldMeta): string | null
}

/**
 * The single "is this value empty" predicate for required checks: undefined, null and strings that are blank after
 * trimming. For a rich text field, markup with no content (empty paragraphs, line breaks) is empty too.
 */
export function isEmpty(v: unknown, field?: { interface: string }): boolean {
  if (v === undefined || v === null) return true
  if (typeof v !== 'string') return false
  return field?.interface === 'richText' ? isBlankMarkup(v) : v.trim() === ''
}
