import type { CollectionMeta, FieldMeta } from '../types/schema'

export type ColumnDef = { field: string; header: string; sortable: boolean }

// camelCase FieldInterface values that render cleanly as a single table cell.
const SCALAR_INTERFACES = new Set<string>([
  'text', 'textarea', 'slug', 'email', 'url', 'phone', 'color',
  'number', 'slider', 'rating',
  'boolean', 'checkbox',
  'date', 'time', 'dateTime',
  'select', 'radio',
])

const MAX_COLUMNS = 6

export function selectListColumns(meta: CollectionMeta): ColumnDef[] {
  const eligible = meta.fields.filter(
    (f) => !f.isSystem && !f.hidden && SCALAR_INTERFACES.has(f.interface),
  )

  const ordered: FieldMeta[] = []
  const ddf = meta.defaultDisplayField
  if (ddf) {
    const hit = eligible.find((f) => f.name === ddf)
    if (hit) ordered.push(hit)
  }
  for (const f of eligible) {
    if (!ordered.includes(f)) ordered.push(f)
  }

  return ordered.slice(0, MAX_COLUMNS).map((f) => ({
    field: f.name,
    header: f.label,
    sortable: f.sortable,
  }))
}
