import { describe, it, expect } from 'vitest'
import { buildRelationTree } from './buildRelationTree'

const rows = [
  { id: 'a', label: 'A', parentId: null },
  { id: 'b', label: 'B', parentId: 'a' },
  { id: 'c', label: 'C', parentId: 'b' },
  { id: 'd', label: 'D', parentId: null },
]

describe('buildRelationTree', () => {
  it('nests rows by parent FK', () => {
    const tree = buildRelationTree(rows, 'parentId')
    expect(tree.map((n) => n.key)).toEqual(['a', 'd'])
    expect(tree[0].children[0].key).toBe('b')
    expect(tree[0].children[0].children[0].key).toBe('c')
  })
  it('excludes the node and its descendants when excludeId is set (cycle guard)', () => {
    const tree = buildRelationTree(rows, 'parentId', 'a')
    // a, b, c all removed; only d remains
    expect(tree.map((n) => n.key)).toEqual(['d'])
  })
})
