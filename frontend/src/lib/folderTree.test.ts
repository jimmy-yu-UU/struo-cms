import { describe, it, expect } from 'vitest'
import { toFolderRows, childFolders, folderPath, type FolderRow } from './folderTree'

const rows: FolderRow[] = [
  { id: 'a', name: 'A', parentId: null },
  { id: 'b', name: 'B', parentId: 'a' },
  { id: 'c', name: 'C', parentId: 'b' },
  { id: 'x', name: 'X', parentId: null },
]

describe('toFolderRows', () => {
  it('maps items rows and defaults missing parentId to null', () => {
    expect(toFolderRows([{ id: 'a', name: 'A' }, { id: 'b', name: 'B', parentId: 'a', version: 3 }]))
      .toEqual([
        { id: 'a', name: 'A', parentId: null, version: undefined },
        { id: 'b', name: 'B', parentId: 'a', version: 3 },
      ])
  })

  // The items API never returns a flat `parentId` column: [CmsRelation] FKs are omitted by
  // ItemProjector on normal responses, and only appear once `deep=parent` expands the relation
  // as a nested `parent: { id, ... }` object under the nav-property name. These cases exercise
  // that real response shape -- a regression back to reading r.parentId directly must fail them.
  it('reads parentId from the deep-expanded nested `parent` object (real API shape)', () => {
    expect(toFolderRows([
      { id: 'a', name: 'A', parent: null },
      { id: 'b', name: 'B', parent: { id: 'a', name: 'A', version: 1 } },
    ])).toEqual([
      { id: 'a', name: 'A', parentId: null, version: undefined },
      { id: 'b', name: 'B', parentId: 'a', version: undefined },
    ])
  })

  it('prefers the nested parent.id over a stray literal parentId when both are present', () => {
    expect(toFolderRows([{ id: 'b', name: 'B', parentId: 'stale', parent: { id: 'a' } }]))
      .toEqual([{ id: 'b', name: 'B', parentId: 'a', version: undefined }])
  })

  it('falls back to a literal parentId when no nested parent is present (back-compat for other callers)', () => {
    expect(toFolderRows([{ id: 'b', name: 'B', parentId: 'a' }]))
      .toEqual([{ id: 'b', name: 'B', parentId: 'a', version: undefined }])
  })
})

describe('childFolders', () => {
  it('returns root folders for null and children for an id', () => {
    expect(childFolders(rows, null).map((f) => f.id)).toEqual(['a', 'x'])
    expect(childFolders(rows, 'a').map((f) => f.id)).toEqual(['b'])
  })
})

describe('folderPath', () => {
  it('returns the ancestor chain root-first', () => {
    expect(folderPath(rows, 'c').map((f) => f.id)).toEqual(['a', 'b', 'c'])
  })
  it('returns [] for null and is cycle-safe', () => {
    expect(folderPath(rows, null)).toEqual([])
    const cyclic: FolderRow[] = [
      { id: 'p', name: 'P', parentId: 'q' }, { id: 'q', name: 'Q', parentId: 'p' }]
    expect(folderPath(cyclic, 'p').map((f) => f.id)).toEqual(['q', 'p'])
  })
})
