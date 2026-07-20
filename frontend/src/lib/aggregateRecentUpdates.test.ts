import { describe, it, expect } from 'vitest'
import { aggregateRecentUpdates, type RecentRow } from './aggregateRecentUpdates'

const row = (id: string, updatedAt: string | null): RecentRow => ({
  id, collection: 'article', collectionLabel: 'Article', title: `T${id}`, updatedAt,
})

describe('aggregateRecentUpdates', () => {
  it('sorts by updatedAt descending and caps at limit', () => {
    const input = [row('a', '2026-07-10T00:00:00Z'), row('b', '2026-07-14T00:00:00Z'), row('c', '2026-07-12T00:00:00Z')]
    expect(aggregateRecentUpdates(input, 2).map((r) => r.id)).toEqual(['b', 'c'])
  })
  it('sorts rows with null/invalid updatedAt last, without throwing', () => {
    const input = [row('a', null), row('b', '2026-07-14T00:00:00Z'), row('c', 'not-a-date')]
    expect(aggregateRecentUpdates(input, 10).map((r) => r.id)).toEqual(['b', 'a', 'c'])
  })
  it('returns a new array and does not mutate the input', () => {
    const input = [row('a', '2026-07-10T00:00:00Z'), row('b', '2026-07-14T00:00:00Z')]
    const out = aggregateRecentUpdates(input, 10)
    expect(out).not.toBe(input)
    expect(input.map((r) => r.id)).toEqual(['a', 'b'])
  })
  it('handles empty input', () => {
    expect(aggregateRecentUpdates([], 8)).toEqual([])
  })
})
