export type ImageAction = 'editAlt' | 'deleteImage'

// Mirrors richTextTableActions.ts: the action name IS its i18n key under `fields.richtext.`.
// Order is load-bearing -- the destructive entries must stay last and contiguous, or
// RichTextContextMenu's separator predicate fires more than once.
export const IMAGE_ACTIONS: ReadonlyArray<ImageAction> = ['editAlt', 'deleteImage']

export const DESTRUCTIVE_IMAGE_ACTIONS: ReadonlySet<ImageAction> = new Set<ImageAction>(['deleteImage'])

/**
 * Whether a right-click landed directly on an <img> that belongs to this editor, returning the
 * element itself (not a boolean) because the caller needs it to resolve a ProseMirror position.
 *
 * Deliberately DOM-only, same reasoning as isInEditorTable: it takes the event target rather than
 * the editor, so it needs no layout and is testable in jsdom.
 *
 * Unlike isInEditorTable, this must NOT walk up via closest(): the image node view wraps every
 * <img> in two container divs, so closest('img') would also match a click that landed on the
 * wrapper beside the image. Only an event target that IS the <img> counts.
 */
export function isEditorImage(target: EventTarget | null, root: HTMLElement): HTMLImageElement | null {
  if (!(target instanceof HTMLImageElement)) return null
  return root.contains(target) ? target : null
}
