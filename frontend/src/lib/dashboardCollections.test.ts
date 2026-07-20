import { describe, it, expect } from 'vitest'
import { SYSTEM_COLLECTIONS, contentCollections } from './dashboardCollections'
import type { CollectionMeta } from '../types/schema'

const coll = (name: string): CollectionMeta => ({
  name, label: name, fields: [], relations: [], defaultDisplayField: null,
})

describe('dashboardCollections', () => {
  it('SYSTEM_COLLECTIONS is file + user', () => {
    expect([...SYSTEM_COLLECTIONS]).toEqual(['file', 'user'])
  })
  it('keeps readable non-system collections', () => {
    const all = [coll('article'), coll('category'), coll('file'), coll('user')]
    const out = contentCollections(all, () => true)
    expect(out.map((c) => c.name)).toEqual(['article', 'category'])
  })
  it('drops collections the user cannot read', () => {
    const all = [coll('article'), coll('secret')]
    const out = contentCollections(all, (n) => n === 'article')
    expect(out.map((c) => c.name)).toEqual(['article'])
  })
})
