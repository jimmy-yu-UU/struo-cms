// Maps a raw revision `operation` (a free-form string from the revisions API; known values are
// create/update/revert) to a vue-i18n key. Unknown values fall back to a generic label so a
// future backend operation never renders a raw token.
const KEYS: Record<string, string> = {
  create: 'revisions.opCreate',
  update: 'revisions.opUpdate',
  revert: 'revisions.opRevert',
}

export function revisionOperationKey(operation: string): string {
  return KEYS[operation] ?? 'revisions.opUnknown'
}

// Shared by the revision list (RevisionHistoryDrawer's node label) and the revision detail pane
// (RevisionSnapshotView's operation label) so the two views cannot silently drift apart on what a
// revert-with-a-known-source renders as. A revert that recorded which revision it restored gets
// the annotated "Reverted (from #n)" copy; every other operation (including a revert with no
// recorded source, e.g. one captured before this field existed) falls back to the plain
// operation label via revisionOperationKey.
export function revisionOperationLabel(
  operation: string,
  sourceRevisionNumber: number | null | undefined,
  t: (key: string, params?: Record<string, unknown>) => string,
): string {
  if (operation === 'revert' && typeof sourceRevisionNumber === 'number') {
    return t('revisions.opRevertFrom', { n: sourceRevisionNumber })
  }
  return t(revisionOperationKey(operation))
}
