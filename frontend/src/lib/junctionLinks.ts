import type { CollectionMeta, FieldMeta, RelationMeta } from '../types/schema'
import type { RelationLink } from '../types/itemForm'
import { getFieldType } from './fieldTypes/registry'
import { relationInputKind } from './relationInputKind'

// Pure helpers behind the junction links editor (spec U2b §3.2). Everything the editor, the
// parse/build/validate helpers and ItemFormView need to agree on lives here so the four of them
// can't drift: which junction fields are editable, when the editor is used at all, and who may
// write payload. `resolve` is the schema store's get(); `access` is the auth store's getters —
// both passed in as plain functions so this module stays store-free and unit-testable.

export type ResolveCollection = (name: string) => CollectionMeta | undefined
export type JunctionAccess = {
  canRead(collection: string): boolean
  canWrite(collection: string): boolean
  isSuperAdmin: boolean
}

// The junction collection's fields named by the relation's junctionPayloadFields, minus Hidden ones
// (the API never returns those in `_junction`, so the SPA must not edit them), in field sort order.
export function visiblePayloadFields(rel: RelationMeta, resolve: ResolveCollection): FieldMeta[] {
  const names = rel.junctionPayloadFields
  if (!rel.junctionCollection || !names || names.length === 0) return []
  const junction = resolve(rel.junctionCollection)
  if (!junction) return []
  const wanted = new Set(names)
  return junction.fields
    .filter((f) => wanted.has(f.name) && !f.hidden)
    .slice()
    .sort((a, b) => a.sort - b.sort)
}

// Maintainer ruling (spec §0 #1): the editor replaces the chip picker when the relation carries
// visible payload OR declares a SortField — a sortable-but-payloadless relation still needs
// somewhere to put its order.
export function usesLinksEditor(rel: RelationMeta, resolve: ResolveCollection): boolean {
  if (relationInputKind(rel.interface) !== 'tagSelect') return false
  return !!rel.sortField || visiblePayloadFields(rel, resolve).length > 0
}

export function canReadJunction(rel: RelationMeta, access: JunctionAccess): boolean {
  return !!rel.junctionCollection && access.canRead(rel.junctionCollection)
}

// Mirrors the server's write-side gate (ItemWriteSideSync.EnsureJunctionPayloadGrant): write grant on
// the junction collection, plus super-admin when it is AdminOnly. Read is a precondition because
// without `_junction` in the response there is nothing truthful to edit.
export function canWriteJunction(rel: RelationMeta, resolve: ResolveCollection, access: JunctionAccess): boolean {
  if (!canReadJunction(rel, access)) return false
  const junction = rel.junctionCollection as string
  if (!access.canWrite(junction)) return false
  const meta = resolve(junction)
  return !(meta?.adminOnly === true && !access.isSuperAdmin)
}

export function emptyLink(id: string, fields: FieldMeta[]): RelationLink {
  const junction: Record<string, unknown> = {}
  for (const f of fields) junction[f.name] = getFieldType(f.interface).defaultValue(f)
  return { id, junction }
}

export function isRelationLinks(v: unknown): v is RelationLink[] {
  return Array.isArray(v) && v.every((x) => x !== null && typeof x === 'object' && 'id' in (x as object))
}
