export type RelationInputKind = 'dropdown' | 'tagSelect' | 'treeSelect' | 'relatedList' | 'readonly'

const MAP: Record<string, RelationInputKind> = {
  dropdown: 'dropdown',
  tagSelect: 'tagSelect',
  treeSelect: 'treeSelect',
  relatedList: 'relatedList',
}

// The relation interfaces this module maps to a real input. Exported so the schema contract test
// (frontend/tests/schemaContract.test.ts) can check it against the backend RelationInterface enum in
// both directions. Checking key presence rather than a 'readonly' return keeps the assertion honest:
// 'readonly' is a legal RelationInputKind, so a member deliberately mapped to it would otherwise be
// indistinguishable from one that fell through.
export const MAPPED_RELATION_INTERFACES: readonly string[] = Object.keys(MAP)

export function relationInputKind(iface: string): RelationInputKind {
  return MAP[iface] ?? 'readonly'
}
