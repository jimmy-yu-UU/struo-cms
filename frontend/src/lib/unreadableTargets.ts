import type { FieldMeta, RelationMeta } from '../types/schema'
import { relationInputKind } from './relationInputKind'
import { FILE_COLLECTION } from './frameworkCollections'

// Answers whether the signed-in caller holds read on a collection.
export type CanReadCollection = (collection: string) => boolean

const FILE_INTERFACES: readonly string[] = ['file', 'image', 'files']

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
  return FILE_INTERFACES.includes(field.interface) && !canRead(FILE_COLLECTION)
}
