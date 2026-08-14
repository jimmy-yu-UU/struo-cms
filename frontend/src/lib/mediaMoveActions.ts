import { itemsApi } from '../api/itemsApi'
import { canMoveFolder, type MovePayload } from './mediaMove'
import type { FolderRow } from './folderTree'

export type MoveResult = { moved: number; skipped: number }

/**
 * Re-parents every item in `payload` under `targetFolderId` (null = root).
 *
 * Folders that would form a cycle are skipped rather than attempted, and counted in `skipped` so the
 * caller can tell the user something was refused. Writes run concurrently and are awaited together,
 * so one failure does not leave the others unissued — the rejection surfaces only after every write
 * has settled, and the caller reloads regardless.
 */
export async function performMove(
  payload: MovePayload,
  targetFolderId: string | null,
  folders: FolderRow[],
): Promise<MoveResult> {
  const movableFolders = payload.folders.filter((id) => canMoveFolder(folders, id, targetFolderId))
  const skipped = payload.folders.length - movableFolders.length

  const writes: Promise<unknown>[] = [
    ...payload.files.map((id) => itemsApi.update('file', id, { folderId: targetFolderId })),
    ...movableFolders.map((id) => itemsApi.update('mediafolder', id, { parentId: targetFolderId })),
  ]

  const settled = await Promise.allSettled(writes)
  const failure = settled.find((s) => s.status === 'rejected')
  if (failure && failure.status === 'rejected') throw failure.reason

  return { moved: writes.length, skipped }
}
