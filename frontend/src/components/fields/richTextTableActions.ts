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

export const IN_TABLE_ACTIONS: ReadonlyArray<readonly [TableAction, string]> = [
  ['addRowBefore', 'Add row above'],
  ['addRowAfter', 'Add row below'],
  ['addColumnBefore', 'Add column left'],
  ['addColumnAfter', 'Add column right'],
  ['deleteRow', 'Delete row'],
  ['deleteColumn', 'Delete column'],
  ['toggleHeaderRow', 'Toggle header row'],
  ['deleteTable', 'Delete table'],
]
