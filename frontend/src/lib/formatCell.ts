import type { FieldMeta } from '../types/schema'
import { getFieldType } from './fieldTypes/registry'

export function formatCell(value: unknown, field: FieldMeta): string {
  if (value === null || value === undefined || value === '') return '—'
  const def = getFieldType(field.interface)
  return def.listColumn ? def.listColumn.format(value, field) : String(value)
}
