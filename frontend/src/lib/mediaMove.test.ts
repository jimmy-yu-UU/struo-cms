import { describe, it, expect } from 'vitest'
import { canMoveFolder, isNoOpMove, serializeMovePayload, parseMovePayload } from './mediaMove'
import type { FolderRow } from './folderTree'

// root → a → b → c ; plus a sibling d at root
const folders: FolderRow[] = [
  { id: 'a', name: 'a', parentId: null },
  { id: 'b', name: 'b', parentId: 'a' },
  { id: 'c', name: 'c', parentId: 'b' },
  { id: 'd', name: 'd', parentId: null },
]

describe('canMoveFolder', () => {
  it('allows a move to an unrelated folder', () => {
    expect(canMoveFolder(folders, 'b', 'd')).toBe(true)
  })

  it('allows a move to the root', () => {
    expect(canMoveFolder(folders, 'c', null)).toBe(true)
  })

  it('refuses moving a folder into itself', () => {
    expect(canMoveFolder(folders, 'a', 'a')).toBe(false)
  })

  it('refuses moving a folder into its direct child', () => {
    expect(canMoveFolder(folders, 'a', 'b')).toBe(false)
  })

  it('refuses moving a folder into a deeper descendant', () => {
    expect(canMoveFolder(folders, 'a', 'c')).toBe(false)
  })

  it('terminates on a pre-existing cycle in the data', () => {
    const cyclic: FolderRow[] = [
      { id: 'x', name: 'x', parentId: 'y' },
      { id: 'y', name: 'y', parentId: 'x' },
    ]
    expect(canMoveFolder(cyclic, 'x', 'y')).toBe(false)
  })

  it('refuses an unknown target', () => {
    expect(canMoveFolder(folders, 'a', 'nope')).toBe(false)
  })
})

describe('isNoOpMove', () => {
  it('detects a move into the folder the item is already in', () => {
    expect(isNoOpMove('a', 'a')).toBe(true)
    expect(isNoOpMove(null, null)).toBe(true)
  })

  it('reports a real move', () => {
    expect(isNoOpMove('a', 'b')).toBe(false)
    expect(isNoOpMove(null, 'a')).toBe(false)
    expect(isNoOpMove('a', null)).toBe(false)
  })
})

describe('drag payload', () => {
  it('round-trips', () => {
    const p = { files: ['f1', 'f2'], folders: ['d1'] }
    expect(parseMovePayload(serializeMovePayload(p))).toEqual(p)
  })

  it('returns null for malformed input', () => {
    expect(parseMovePayload('')).toBeNull()
    expect(parseMovePayload('not json')).toBeNull()
    expect(parseMovePayload('{"files":"nope"}')).toBeNull()
    expect(parseMovePayload('[]')).toBeNull()
  })

  it('coerces missing arrays to empty ones', () => {
    expect(parseMovePayload('{"files":["f1"]}')).toEqual({ files: ['f1'], folders: [] })
  })
})
