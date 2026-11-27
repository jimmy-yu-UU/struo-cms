import type { CollectionMeta, FieldMeta, FieldOption, RelationMeta } from '../types/schema'

export type CoreCollectionLabels = {
  label: string
  fields: Record<string, string>
  relations?: Record<string, string>
  options?: Record<string, Record<string, string>>
}

export type CoreLabelCatalog = {
  groups: Record<string, string>
  collections: Record<string, CoreCollectionLabels>
}

// `source` holds the English the server sends for the core collections; `target` the text to show.
export type CoreLabelMessages = { source: CoreLabelCatalog; target: CoreLabelCatalog }

function own<T>(record: Record<string, T> | undefined, key: string): T | undefined {
  return record !== undefined && Object.hasOwn(record, key) ? record[key] : undefined
}

// The target text replaces the server label only when the label still equals the source text, so a
// label a fork changed is shown as the server sends it.
function swap(serverLabel: string, source: string | undefined, target: string | undefined): string {
  return source !== undefined && target && serverLabel === source ? target : serverLabel
}

function localizeOptions(
  options: FieldOption[] | null | undefined,
  source: Record<string, string> | undefined,
  target: Record<string, string> | undefined,
): FieldOption[] | null | undefined {
  return options?.map((o) => ({ ...o, label: swap(o.label, own(source, o.value), own(target, o.value)) }))
}

function localizeField(f: FieldMeta, src: CoreCollectionLabels, tgt: CoreCollectionLabels | undefined): FieldMeta {
  const localized: FieldMeta = { ...f, label: swap(f.label, own(src.fields, f.name), own(tgt?.fields, f.name)) }
  if (f.options) {
    localized.options = localizeOptions(f.options, own(src.options, f.name), own(tgt?.options, f.name))
  }
  return localized
}

function localizeRelation(r: RelationMeta, src: CoreCollectionLabels, tgt: CoreCollectionLabels | undefined): RelationMeta {
  return { ...r, label: swap(r.label, own(src.relations, r.name), own(tgt?.relations, r.name)) }
}

function localizeCollection(c: CollectionMeta, { source, target }: CoreLabelMessages): CollectionMeta {
  const src = own(source.collections, c.name)
  const tgt = own(target.collections, c.name)
  const group = c.group ? swap(c.group, own(source.groups, c.group), own(target.groups, c.group)) : c.group
  if (!src) return group === c.group ? c : { ...c, group }
  return {
    ...c,
    label: swap(c.label, src.label, tgt?.label),
    group,
    fields: c.fields.map((f) => localizeField(f, src, tgt)),
    relations: c.relations.map((r) => localizeRelation(r, src, tgt)),
  }
}

// Returns a localized copy; the input is never changed. A group name is shared by every collection
// in it, so a fork collection placed in a core group shows the translated group name.
export function localizeCoreSchema(collections: CollectionMeta[], messages: CoreLabelMessages): CollectionMeta[] {
  return collections.map((c) => localizeCollection(c, messages))
}
