import type { MovePayload } from './mediaMove'

/**
 * Toggles `id` into or out of the `kind` bucket of `sel`, returning a NEW MovePayload.
 *
 * Files and folders are tracked in separate arrays, so the same id can independently be a
 * selected file AND a selected folder without either toggle affecting the other.
 *
 * Never mutates `sel` or either of its arrays -- both `files` and `folders` on the returned
 * object are fresh array references every call (not just the bucket that actually changed),
 * matching this repository's immutable-by-default rule.
 */
export function toggleSelection(sel: MovePayload, kind: 'file' | 'folder', id: string): MovePayload {
  const files = kind === 'file' ? toggled(sel.files, id) : [...sel.files]
  const folders = kind === 'folder' ? toggled(sel.folders, id) : [...sel.folders]
  return { files, folders }
}

function toggled(ids: string[], id: string): string[] {
  return ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]
}

/** Total number of selected items across both buckets. */
export function selectionCount(sel: MovePayload): number {
  return sel.files.length + sel.folders.length
}
