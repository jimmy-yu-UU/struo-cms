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
