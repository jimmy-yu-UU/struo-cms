export type TableAction =
  | 'insert'
  | 'addRowBefore'
  | 'addRowAfter'
  | 'addColumnBefore'
  | 'addColumnAfter'
  | 'deleteRow'
  | 'deleteColumn'
  | 'toggleHeaderRow'
  | 'deleteTable'

// Each action's i18n key under `fields.richtext.` IS its action name, so the list carries the
// action alone and the call site derives the key. Keeping them structurally identical is what
// makes a mismatch impossible rather than merely unlikely.
//
// Order is load-bearing: toggleHeaderRow sits BEFORE the three deletes so the destructive entries
// stay contiguous and the context menu's separator predicate fires exactly once.
export const IN_TABLE_ACTIONS: ReadonlyArray<TableAction> = [
  'addRowBefore',
  'addRowAfter',
  'addColumnBefore',
  'addColumnAfter',
  'toggleHeaderRow',
  'deleteRow',
  'deleteColumn',
  'deleteTable',
]

// The context menu (a later slice) will place a separator before the first of these; the set
// lives here so that menu does not hard-code an index.
export const DESTRUCTIVE_TABLE_ACTIONS: ReadonlySet<TableAction> =
  new Set<TableAction>(['deleteRow', 'deleteColumn', 'deleteTable'])

export const TABLE_SIZE_MIN = 1
export const TABLE_SIZE_MAX = 20

/**
 * Whether a right-click landed inside a table that belongs to this editor.
 *
 * Deliberately DOM-only: it takes the event target rather than the editor, so it needs no layout
 * and is testable in jsdom. Resolving the clicked cell to a ProseMirror position is a separate
 * step that does need layout — see RichTextInput's context-menu handler.
 */
export function isInEditorTable(target: EventTarget | null, root: HTMLElement): boolean {
  if (!(target instanceof Node)) return false
  const el = target instanceof HTMLElement ? target : target.parentElement
  const table = el?.closest('table')
  return !!table && root.contains(table)
}
