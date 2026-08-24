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

// Second element is an i18n key under `fields.richtext.`, not a label: the call site translates it.
export const IN_TABLE_ACTIONS: ReadonlyArray<readonly [TableAction, string]> = [
  ['addRowBefore', 'addRowBefore'],
  ['addRowAfter', 'addRowAfter'],
  ['addColumnBefore', 'addColumnBefore'],
  ['addColumnAfter', 'addColumnAfter'],
  ['deleteRow', 'deleteRow'],
  ['deleteColumn', 'deleteColumn'],
  ['toggleHeaderRow', 'toggleHeaderRow'],
  ['deleteTable', 'deleteTable'],
]

// Destructive entries are separated from the additive ones in the menu; this is where the split
// lives so the menu component does not hard-code an index.
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
