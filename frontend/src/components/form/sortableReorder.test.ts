import { describe, it, expect } from 'vitest'
import { reorder } from './sortableReorder'

describe('reorder', () => {
  it('moves an item forward (from < to)', () => {
    expect(reorder(['a', 'b', 'c'], 0, 2)).toEqual(['b', 'c', 'a'])
  })

  it('moves an item backward (from > to)', () => {
    expect(reorder(['a', 'b', 'c'], 2, 0)).toEqual(['c', 'a', 'b'])
  })

  it('is a no-op reorder when from === to, but still returns a new array', () => {
    const list = ['a', 'b', 'c']
    const next = reorder(list, 1, 1)
    expect(next).toEqual(['a', 'b', 'c'])
    expect(next).not.toBe(list)
  })

  it('never mutates the input array', () => {
    const list = ['a', 'b', 'c']
    reorder(list, 0, 2)
    expect(list).toEqual(['a', 'b', 'c'])
  })

  it.each([
    ['from negative', -1, 1],
    ['from at length', 3, 1],
    ['from past length', 5, 1],
    ['to negative', 1, -1],
    ['to at length', 1, 3],
    ['to past length', 1, 5],
    ['both negative', -1, -1],
    ['both past length', 5, 5],
    ['from negative, to past length', -1, 5],
  ])('returns the SAME reference, unchanged, when %s (from=%i, to=%i)', (_label, from, to) => {
    const list = ['a', 'b', 'c']
    const next = reorder(list, from, to)
    expect(next).toBe(list)
    expect(next).toEqual(['a', 'b', 'c'])
  })

  it('returns the same (empty) reference for an empty list regardless of indices', () => {
    const list: string[] = []
    expect(reorder(list, 0, 0)).toBe(list)
    expect(reorder(list, -1, -1)).toBe(list)
  })

  it('refuses a move on a single-item list (both indices out of the only valid slot 0 would be fine, but out-of-range still refused)', () => {
    const list = ['solo']
    expect(reorder(list, 0, 1)).toBe(list)
    expect(reorder(list, 1, 0)).toBe(list)
    expect(reorder(list, 0, 0)).toEqual(['solo'])
  })
})
