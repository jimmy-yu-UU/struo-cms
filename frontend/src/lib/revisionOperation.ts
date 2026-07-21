// Maps a raw revision `operation` (a free-form string from the 9c API; known values are
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
