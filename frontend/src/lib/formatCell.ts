import type { FieldMeta } from '../types/schema'
import { getFieldType } from './fieldTypes/registry'
import { toDisplayString } from './toDisplayString'

export function formatCell(value: unknown, field: FieldMeta): string {
  if (value === null || value === undefined || value === '') return '—'
  const def = getFieldType(field.interface)
  return def.listColumn ? def.listColumn.format(value, field) : toDisplayString(value)
}
