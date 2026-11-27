import type { CollectionMeta, FieldMeta, RelationMeta } from '../types/schema'
import { splitFields } from './splitFields'
import { relationInputKind } from './relationInputKind'
import { FILE_COLLECTION } from './frameworkCollections'

// Answers whether the signed-in caller holds read on a collection.
export type CanReadCollection = (collection: string) => boolean

const FILE_INTERFACES: ReadonlySet<string> = new Set(['file', 'image', 'files'])

// The server refuses a write that names a relation key, or a file key shared or inside a locale, whose
// target collection the caller cannot read, so the form leaves such a key out and shows the field disabled.

// A relation the item form writes (a foreign key or a link array) and whose target the caller cannot
// read. Without a read check every target counts as readable.
export function isRelationTargetUnreadable(rel: RelationMeta, canRead?: CanReadCollection): boolean {
  if (!canRead) return false
  const kind = relationInputKind(rel.interface)
  const written = kind === 'dropdown' || kind === 'treeSelect' || kind === 'tagSelect'
  return written && !canRead(rel.targetCollection)
}

// A file, image or files field, shared or translatable, whose target is the file collection, which the
// caller cannot read.
export function isFileFieldUnreadable(field: FieldMeta, canRead?: CanReadCollection): boolean {
  if (!canRead) return false
  return FILE_INTERFACES.has(field.interface) && !canRead(FILE_COLLECTION)
}

// A relation of a kind the server checks on a write, whatever interface shows it: a many-to-one with a
// foreign key, or a many-to-many. Its target is one the caller cannot read.
export function isRelationLeftByRevert(rel: RelationMeta, canRead?: CanReadCollection): boolean {
  if (!canRead) return false
  const linked = (rel.kind === 'manyToOne' && !!rel.foreignKey) || rel.kind === 'manyToMany'
  return linked && !canRead(rel.targetCollection)
}

// The labels of the fields a revert leaves as they are for this caller: the file fields (shared and
// translatable) and the relations, chosen by kind as the server chooses them, whose target the caller
// cannot read. A read-only field never changes on a write, so it is not listed.
export function fieldsLeftByRevert(meta: CollectionMeta, canRead?: CanReadCollection): string[] {
  const { shared, translatable } = splitFields(meta)
  const fields = [...shared, ...translatable].filter((f) => !f.readOnly && isFileFieldUnreadable(f, canRead))
  const relations = (meta.relations ?? []).filter((rel) => isRelationLeftByRevert(rel, canRead))
  return [...fields.map((f) => f.label), ...relations.map((rel) => rel.label)]
}
