import type { FolderRow } from './folderTree'

/** Custom MIME so a drag originating outside the media library is ignored. */
export const DRAG_MIME = 'application/x-struo-media'

export type MovePayload = { files: string[]; folders: string[] }

/**
 * Whether `sourceId` may be re-parented under `targetId` (null = root).
 *
 * Walks up from the target to the root: if the source appears anywhere on that chain, the target is
 * a descendant of the source and the move would detach that subtree from the tree entirely.
 * `folderPath` is cycle-safe on the read side, but nothing there stops the cycle being written in
 * the first place — that is this function's job. The walk is bounded by a seen-set so malformed
 * data that already contains a cycle terminates instead of hanging.
 */
export function canMoveFolder(folders: FolderRow[], sourceId: string, targetId: string | null): boolean {
  if (targetId === null) return true
  if (targetId === sourceId) return false
  const byId = new Map(folders.map((f) => [f.id, f]))
  if (!byId.has(targetId)) return false
  const seen = new Set<string>()
  let cur: string | null = targetId
  while (cur !== null && !seen.has(cur)) {
    if (cur === sourceId) return false
    seen.add(cur)
    cur = byId.get(cur)?.parentId ?? null
  }
  return true
}

/** A move into the parent the item already has changes nothing and must not issue a write. */
export function isNoOpMove(currentParentId: string | null, targetId: string | null): boolean {
  return currentParentId === targetId
}

export function serializeMovePayload(p: MovePayload): string {
  return JSON.stringify({ files: p.files, folders: p.folders })
}

function stringArray(v: unknown): string[] | null {
  if (v === undefined) return []
  if (!Array.isArray(v)) return null
  return v.every((x) => typeof x === 'string') ? (v as string[]) : null
}

export function parseMovePayload(raw: string): MovePayload | null {
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return null
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return null
  const rec = parsed as Record<string, unknown>
  const files = stringArray(rec.files)
  const folders = stringArray(rec.folders)
  if (files === null || folders === null) return null
  return { files, folders }
}
