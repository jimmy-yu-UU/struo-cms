export type DeleteKind = 'soft' | 'hard'

export function deleteKindFor(meta: { softDelete?: boolean } | null | undefined): DeleteKind {
  return meta?.softDelete ? 'soft' : 'hard'
}

export function deleteConfirm(kind: DeleteKind): { header: string; message: string } {
  return kind === 'soft'
    ? { header: 'Move to trash', message: 'Move this item to trash? You can restore it later.' }
    : { header: 'Confirm delete', message: 'Delete this item? This cannot be undone.' }
}

export function purgeConfirm(): { header: string; message: string } {
  return { header: 'Delete permanently', message: 'Permanently delete this item? This cannot be undone.' }
}
