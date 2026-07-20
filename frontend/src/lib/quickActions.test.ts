import { describe, it, expect } from 'vitest'
import { buildQuickActions } from './quickActions'
import type { CollectionMeta } from '../types/schema'

const coll = (name: string, label = name): CollectionMeta => ({
  name, label, fields: [], relations: [], defaultDisplayField: null,
})

describe('buildQuickActions', () => {
  it('includes uploadMedia only when the user can write files', () => {
    expect(buildQuickActions([], (n) => n === 'file', 3)).toEqual([{ kind: 'uploadMedia' }])
    expect(buildQuickActions([], () => false, 3)).toEqual([])
  })
  it('emits a newItem action per writable content collection', () => {
    const all = [coll('article', 'Article'), coll('category', 'Category')]
    const out = buildQuickActions(all, () => true, 3)
    expect(out).toContainEqual({ kind: 'newItem', collection: 'article', label: 'Article' })
    expect(out).toContainEqual({ kind: 'newItem', collection: 'category', label: 'Category' })
  })
  it('excludes system collections from newItem actions', () => {
    const all = [coll('file'), coll('user')]
    const out = buildQuickActions(all, () => true, 3)
    expect(out.some((a) => a.kind === 'newItem')).toBe(false)
  })
  it('caps newItem actions at maxNewItems', () => {
    const all = [coll('a'), coll('b'), coll('c'), coll('d')]
    const out = buildQuickActions(all, (n) => n !== 'file', 2)
    expect(out.filter((a) => a.kind === 'newItem')).toHaveLength(2)
  })
})
