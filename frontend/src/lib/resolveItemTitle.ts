import type { CollectionMeta } from '../types/schema'
import { pickTranslated } from './pickTranslated'

export type TitleRow = Record<string, unknown> & {
  id?: unknown
  translations?: Record<string, Record<string, unknown>>
}

function camel(s: string): string {
  return s.length ? s[0].toLowerCase() + s.slice(1) : s
}

export function readField(
  row: TitleRow,
  targetMeta: CollectionMeta,
  locale: string,
  fieldKey: string,
): unknown {
  const field = targetMeta.fields.find((f) => f.name.toLowerCase() === fieldKey.toLowerCase())
  const key = field?.name ?? camel(fieldKey)
  if (field?.translatable) {
    return pickTranslated(row.translations, locale, key)
  }
  return row[key]
}

export function resolveItemTitle(row: TitleRow, targetMeta: CollectionMeta, locale: string): string {
  if (targetMeta.defaultDisplayField) {
    const v = readField(row, targetMeta, locale, targetMeta.defaultDisplayField)
    if (v != null && v !== '') return String(v)
  }
  return row.id != null ? String(row.id) : ''
}
