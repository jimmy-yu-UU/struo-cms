export type ImageAction = 'editAlt' | 'deleteImage'

// Mirrors richTextTableActions.ts (TableAction/IN_TABLE_ACTIONS): the action name IS its i18n key
// under `fields.richtext.`, so the call site derives the key from the action itself rather than
// carrying a second, parallel key that could drift out of sync.
//
// Order is load-bearing here too: editAlt sits BEFORE the sole destructive entry so the context
// menu's separator predicate (RichTextContextMenu's needsSeparator) fires exactly once. That
// predicate is shared across both action kinds, so the same "destructive entries must stay
// contiguous" constraint IN_TABLE_ACTIONS documents applies to this list as well -- interleaving
// a non-destructive entry between destructive ones would make the predicate fire more than once.
export const IMAGE_ACTIONS: ReadonlyArray<ImageAction> = ['editAlt', 'deleteImage']

export const DESTRUCTIVE_IMAGE_ACTIONS: ReadonlySet<ImageAction> = new Set<ImageAction>(['deleteImage'])

/**
 * Whether a right-click landed directly on an <img> that belongs to this editor, returning the
 * element itself (not a boolean) because the caller needs it to resolve a ProseMirror position.
 *
 * Deliberately DOM-only, same reasoning as isInEditorTable: it takes the event target rather than
 * the editor, so it needs no layout and is testable in jsdom.
 *
 * Unlike isInEditorTable, this does NOT walk up via closest(). Task 2's image node view wraps
 * every <img> in two container divs ([data-resize-container] > [data-resize-wrapper]), with the
 * resize-handle divs as siblings of the <img> inside that wrapper -- closest('img') would still
 * find the image from a click that landed on the wrapper's own padding next to it, but that is not
 * a click on the image and must not be treated as one. Only an event target that IS the <img>
 * element counts.
 */
export function isEditorImage(target: EventTarget | null, root: HTMLElement): HTMLImageElement | null {
  if (!(target instanceof Node)) return null
  if (!(target instanceof HTMLImageElement)) return null
  return root.contains(target) ? target : null
}
