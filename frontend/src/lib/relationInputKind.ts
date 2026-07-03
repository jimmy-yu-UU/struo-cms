export type RelationInputKind = 'dropdown' | 'tagSelect' | 'treeSelect' | 'relatedList' | 'readonly'

const MAP: Record<string, RelationInputKind> = {
  dropdown: 'dropdown',
  tagSelect: 'tagSelect',
  treeSelect: 'treeSelect',
  relatedList: 'relatedList',
}

export function relationInputKind(iface: string): RelationInputKind {
  return MAP[iface] ?? 'readonly'
}
