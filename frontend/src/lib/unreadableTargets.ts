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

const SOFT_DELETE_KEYS = ['deletedat', 'deletedby']

// The lower-cased names of the keys a body never sets: the read-only and audit fields, and the soft-delete
// columns of a soft-deletable collection.
function serverOwnedKeys(meta: CollectionMeta): Set<string> {
  const names = meta.fields.filter((f) => f.readOnly || f.isSystem).map((f) => f.name.toLowerCase())
  return new Set(meta.softDelete ? [...names, ...SOFT_DELETE_KEYS] : names)
}

// A relation a revert writes and the server checks, whatever interface shows it: a many-to-one with a
// foreign key that is not a server-owned key, or a many-to-many. A server-owned key is never written, so a
// revert leaves it as it is for every caller; `editable` only locks the admin picker and is not consulted.
// Its target is one the caller cannot read.
export function isRelationLeftByRevert(
  rel: RelationMeta, serverOwned: ReadonlySet<string>, canRead?: CanReadCollection,
): boolean {
  if (!canRead) return false
  const written = rel.kind === 'manyToMany' || (rel.kind === 'manyToOne' && !!rel.foreignKey && !serverOwned.has(rel.foreignKey.toLowerCase()))
  return written && !canRead(rel.targetCollection)
}

// The labels of the fields a revert writes for other callers and leaves as they are for this one: the
// file fields (shared and translatable) and the relations, chosen by kind as the server chooses them,
// whose target the caller cannot read. A read-only field, or a relation on a server-owned key, is never
// written, so it is not listed.
export function fieldsLeftByRevert(meta: CollectionMeta, canRead?: CanReadCollection): string[] {
  const { shared, translatable } = splitFields(meta)
  const fields = [...shared, ...translatable].filter((f) => !f.readOnly && isFileFieldUnreadable(f, canRead))
  const serverOwned = serverOwnedKeys(meta)
  const relations = (meta.relations ?? []).filter((rel) => isRelationLeftByRevert(rel, serverOwned, canRead))
  return [...fields.map((f) => f.label), ...relations.map((rel) => rel.label)]
}
