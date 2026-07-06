import type { Component } from 'vue'
import type { FieldMeta } from '../../types/schema'

// Mirrors backend Struo.Domain.Metadata.Enums.FieldInterface (camelCase).
// MUST be kept in sync with that enum — adding a backend interface requires adding it here.
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

export function isEmpty(v: unknown): boolean {
  return v === undefined || v === null || v === ''
}
