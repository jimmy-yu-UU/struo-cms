import type { CollectionMeta, FieldMeta } from '../types/schema'

export type SplitFields = { shared: FieldMeta[]; translatable: FieldMeta[] }

export function splitFields(meta: CollectionMeta): SplitFields {
  const editable = meta.fields
    .filter((f) => !f.isSystem)
    .slice()
    .sort((a, b) => a.sort - b.sort)
  return {
    shared: editable.filter((f) => !f.translatable),
    translatable: editable.filter((f) => f.translatable),
  }
}
