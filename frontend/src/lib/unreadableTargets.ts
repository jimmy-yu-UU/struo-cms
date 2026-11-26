import type { FieldMeta, RelationMeta } from '../types/schema'
import { relationInputKind } from './relationInputKind'
import { FILE_COLLECTION } from './frameworkCollections'

// Answers whether the signed-in caller holds read on a collection.
export type CanReadCollection = (collection: string) => boolean

const FILE_INTERFACES: readonly string[] = ['file', 'image', 'files']

// The server refuses a write that names a relation or file field whose target collection the caller
// cannot read, so the form leaves such a key out and shows the field disabled.

// A relation the item form writes (a foreign key or a link array) and whose target the caller cannot
// read. Without a read check every target counts as readable.
export function isRelationTargetUnreadable(rel: RelationMeta, canRead?: CanReadCollection): boolean {
  if (!canRead) return false
  const kind = relationInputKind(rel.interface)
  const written = kind === 'dropdown' || kind === 'treeSelect' || kind === 'tagSelect'
  return written && !canRead(rel.targetCollection)
}

// A non-translatable file, image or files field whose target is the file collection, which the caller
// cannot read. Translatable file fields are not covered by the server rule.
export function isFileFieldUnreadable(field: FieldMeta, canRead?: CanReadCollection): boolean {
  if (!canRead || field.translatable) return false
  return FILE_INTERFACES.includes(field.interface) && !canRead(FILE_COLLECTION)
}
