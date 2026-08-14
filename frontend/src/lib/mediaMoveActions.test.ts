import { describe, it, expect, vi, beforeEach } from 'vitest'
import { performMove } from './mediaMoveActions'
import { itemsApi } from '../api/itemsApi'
import type { FolderRow } from './folderTree'

vi.mock('../api/itemsApi', () => ({ itemsApi: { update: vi.fn() } }))
const update = vi.mocked(itemsApi.update)

const folders: FolderRow[] = [
  { id: 'a', name: 'a', parentId: null },
  { id: 'b', name: 'b', parentId: 'a' },
  { id: 'd', name: 'd', parentId: null },
]

beforeEach(() => { update.mockReset(); update.mockResolvedValue({}) })

describe('performMove', () => {
  it('moves files by setting folderId', async () => {
    const r = await performMove({ files: ['f1', 'f2'], folders: [] }, 'd', folders)
    expect(update).toHaveBeenCalledWith('file', 'f1', { folderId: 'd' })
    expect(update).toHaveBeenCalledWith('file', 'f2', { folderId: 'd' })
    expect(r).toEqual({ moved: 2, skipped: 0 })
  })

  it('moves folders by setting parentId', async () => {
    const r = await performMove({ files: [], folders: ['b'] }, 'd', folders)
    expect(update).toHaveBeenCalledWith('mediafolder', 'b', { parentId: 'd' })
    expect(r).toEqual({ moved: 1, skipped: 0 })
  })

  it('sends null for a move to the root', async () => {
    await performMove({ files: ['f1'], folders: ['b'] }, null, folders)
    expect(update).toHaveBeenCalledWith('file', 'f1', { folderId: null })
    expect(update).toHaveBeenCalledWith('mediafolder', 'b', { parentId: null })
  })

  it('skips a folder that would form a cycle and still moves the rest', async () => {
    const r = await performMove({ files: ['f1'], folders: ['a'] }, 'b', folders)
    expect(update).toHaveBeenCalledWith('file', 'f1', { folderId: 'b' })
    expect(update).not.toHaveBeenCalledWith('mediafolder', 'a', expect.anything())
    expect(r).toEqual({ moved: 1, skipped: 1 })
  })

  it('rejects when a write fails', async () => {
    update.mockRejectedValueOnce(new Error('boom'))
    await expect(performMove({ files: ['f1'], folders: [] }, 'd', folders)).rejects.toThrow('boom')
  })
})
