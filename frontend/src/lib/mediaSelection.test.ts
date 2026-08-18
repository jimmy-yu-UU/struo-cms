import { describe, it, expect } from 'vitest'
import { toggleSelection, selectionCount, removeFromSelection } from './mediaSelection'
import type { MovePayload } from './mediaMove'

describe('toggleSelection', () => {
  it('adds an unselected file', () => {
    const sel: MovePayload = { files: [], folders: [] }
    expect(toggleSelection(sel, 'file', 'f1')).toEqual({ files: ['f1'], folders: [] })
  })

  it('removes an already-selected file', () => {
    const sel: MovePayload = { files: ['f1'], folders: [] }
    expect(toggleSelection(sel, 'file', 'f1')).toEqual({ files: [], folders: [] })
  })

  it('adds an unselected folder', () => {
    const sel: MovePayload = { files: [], folders: [] }
    expect(toggleSelection(sel, 'folder', 'd1')).toEqual({ files: [], folders: ['d1'] })
  })

  it('removes an already-selected folder', () => {
    const sel: MovePayload = { files: [], folders: ['d1'] }
    expect(toggleSelection(sel, 'folder', 'd1')).toEqual({ files: [], folders: [] })
  })

  it('tracks files and folders independently -- the same id in both kinds does not collide', () => {
    const empty: MovePayload = { files: [], folders: [] }
    const afterFile = toggleSelection(empty, 'file', 'shared')
    const afterBoth = toggleSelection(afterFile, 'folder', 'shared')
    expect(afterBoth).toEqual({ files: ['shared'], folders: ['shared'] })
    // Turning the file bucket back off must leave the folder bucket (which happens to share the
    // same id) untouched.
    const afterFileOff = toggleSelection(afterBoth, 'file', 'shared')
    expect(afterFileOff).toEqual({ files: [], folders: ['shared'] })
  })

  it('does not mutate the input object or either of its arrays', () => {
    const files = ['f1']
    const folders = ['d1']
    const sel: MovePayload = { files, folders }
    const next = toggleSelection(sel, 'file', 'f2')

    // The original object's own fields must read exactly as they did before the call.
    expect(sel).toEqual({ files: ['f1'], folders: ['d1'] })
    // Not merely unchanged in value -- the original arrays must be the SAME references, i.e.
    // never spliced/pushed into.
    expect(sel.files).toBe(files)
    expect(sel.folders).toBe(folders)
    // The result must be a distinct object with distinct arrays, not the input handed back.
    expect(next).not.toBe(sel)
    expect(next.files).not.toBe(sel.files)
    expect(next.folders).not.toBe(sel.folders)
  })

  it('returns fresh array references even when only one side actually changes', () => {
    const files: string[] = []
    const folders: string[] = []
    const sel: MovePayload = { files, folders }
    const next = toggleSelection(sel, 'file', 'f1')
    // folders was untouched by this call but must still be a new array, not the same reference,
    // per the repository's immutability rule ("new arrays and a new object every call").
    expect(next.folders).not.toBe(folders)
    expect(next.folders).toEqual([])
  })
})

describe('removeFromSelection', () => {
  it('removes an id from the file bucket, leaving folders untouched', () => {
    const sel: MovePayload = { files: ['f1', 'f2'], folders: ['d1'] }
    expect(removeFromSelection(sel, 'file', 'f1')).toEqual({ files: ['f2'], folders: ['d1'] })
  })

  it('removes an id from the folder bucket, leaving files untouched', () => {
    const sel: MovePayload = { files: ['f1'], folders: ['d1', 'd2'] }
    expect(removeFromSelection(sel, 'folder', 'd1')).toEqual({ files: ['f1'], folders: ['d2'] })
  })

  it('is a no-op when the id is not present in that bucket', () => {
    const sel: MovePayload = { files: ['f1'], folders: [] }
    expect(removeFromSelection(sel, 'file', 'nope')).toEqual({ files: ['f1'], folders: [] })
  })

  it('does not remove a same-valued id from the OTHER bucket', () => {
    const sel: MovePayload = { files: ['shared'], folders: ['shared'] }
    expect(removeFromSelection(sel, 'file', 'shared')).toEqual({ files: [], folders: ['shared'] })
  })

  it('does not mutate the input object or either of its arrays', () => {
    const files = ['f1']
    const folders = ['d1']
    const sel: MovePayload = { files, folders }
    const next = removeFromSelection(sel, 'file', 'f1')
    expect(sel).toEqual({ files: ['f1'], folders: ['d1'] })
    expect(sel.files).toBe(files)
    expect(sel.folders).toBe(folders)
    expect(next).not.toBe(sel)
    expect(next.files).not.toBe(sel.files)
    expect(next.folders).not.toBe(sel.folders)
  })
})

describe('selectionCount', () => {
  it('sums files and folders', () => {
    expect(selectionCount({ files: ['a', 'b'], folders: ['c'] })).toBe(3)
  })

  it('is zero for an empty selection', () => {
    expect(selectionCount({ files: [], folders: [] })).toBe(0)
  })
})
