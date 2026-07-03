import type { CollectionMeta, RelationMeta } from '../types/schema'

type Row = Record<string, unknown> & { id?: unknown; translations?: Record<string, Record<string, unknown>> }

function camel(s: string): string {
  return s.length ? s[0].toLowerCase() + s.slice(1) : s
}

function readField(row: Row, targetMeta: CollectionMeta, locale: string, fieldKey: string): unknown {
  const field = targetMeta.fields.find((f) => f.name.toLowerCase() === fieldKey.toLowerCase())
  const key = field?.name ?? camel(fieldKey)
  if (field?.translatable) {
    const t = row.translations?.[locale]
    return t?.[key]
  }
  return row[key]
}

export function resolveDisplayLabel(
  row: Row,
  relation: RelationMeta,
  targetMeta: CollectionMeta,
  locale: string,
): string {
  const template = relation.displayTemplate
  if (template) {
    const out = template.replace(/\{(\w+)\}/g, (_m, token: string) => {
      const v = readField(row, targetMeta, locale, token)
      return v == null || v === '' ? '' : String(v)
    })
    if (out.trim() !== '') return out
  }
  if (targetMeta.defaultDisplayField) {
    const v = readField(row, targetMeta, locale, targetMeta.defaultDisplayField)
    if (v != null && v !== '') return String(v)
  }
  return row.id != null ? String(row.id) : ''
}
