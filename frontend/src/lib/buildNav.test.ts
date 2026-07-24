import { describe, it, expect } from 'vitest'
import { buildNav } from './buildNav'
import type { CollectionMeta, CollectionPermission } from '../types/schema'

function coll(name: string, group?: string): CollectionMeta {
  return { name, label: name[0].toUpperCase() + name.slice(1), group, fields: [], relations: [] }
}
const grant = (read: boolean): CollectionPermission => ({ read, write: false, delete: false })

describe('buildNav', () => {
  const collections = [coll('article', 'Content'), coll('category', 'Content'), coll('user')]

  it('super-admin sees every collection', () => {
    const nav = buildNav(collections, true, {})
    const names = nav.flatMap((g) => g.items.map((i) => i.name))
    expect(names.sort()).toEqual(['article', 'category', 'user'])
  })

  it('non-super users see only readable collections', () => {
    const nav = buildNav(collections, false, { article: grant(true), category: grant(false) })
    const names = nav.flatMap((g) => g.items.map((i) => i.name))
    expect(names).toEqual(['article'])
  })

  it('groups by group, defaulting blank/missing to General', () => {
    const nav = buildNav(collections, true, {})
    const content = nav.find((g) => g.group === 'Content')!
    const general = nav.find((g) => g.group === 'General')!
    expect(content.items.map((i) => i.name)).toEqual(['article', 'category'])
    expect(general.items.map((i) => i.name)).toEqual(['user'])
  })

  it('suppresses the file collection from auto nav when marked hidden', () => {
    const cols = [
      { name: 'file', label: 'File', group: 'System', hidden: true },
      { name: 'article', label: 'Article', group: 'Content' },
    ] as never
    const groups = buildNav(cols, true, {})
    const names = groups.flatMap((g) => g.items.map((i) => i.name))
    expect(names).toContain('article')
    expect(names).not.toContain('file')
  })

  it('hidden collections never appear in nav, even for super-admins', () => {
    const collections = [
      { name: 'article', label: 'Article', fields: [], relations: [] },
      { name: 'permission', label: 'Permission', hidden: true, fields: [], relations: [] },
    ] as any
    const nav = buildNav(collections, true, {})
    const names = nav.flatMap((g) => g.items.map((i) => i.name))
    expect(names).toContain('article')
    expect(names).not.toContain('permission')
  })

  it('file visibility is driven by the hidden flag, not a hardcoded name', () => {
    const collections = [
      { name: 'file', label: 'File', fields: [], relations: [] }, // not hidden -> visible
    ] as any
    const nav = buildNav(collections, true, {})
    expect(nav.flatMap((g) => g.items.map((i) => i.name))).toContain('file')
  })
})
