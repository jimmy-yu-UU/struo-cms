import { describe, it, expect } from 'vitest'
import { buildBreadcrumb } from './buildBreadcrumb'
import type { CollectionMeta } from '../types/schema'

const t = (k: string) => k // identity: assert against keys
const collections: CollectionMeta[] = [
  { name: 'article', label: 'Article', group: 'Content', fields: [], relations: [] },
  { name: 'page', label: 'Page', group: null, fields: [], relations: [] },
]

describe('buildBreadcrumb', () => {
  it('dashboard is a single current crumb', () => {
    const c = buildBreadcrumb({ name: 'dashboard', params: {} }, collections, t)
    expect(c).toEqual([{ label: 'nav.dashboard' }])
  })

  it('media is dashboard > media', () => {
    const c = buildBreadcrumb({ name: 'media', params: {} }, collections, t)
    expect(c).toEqual([{ label: 'nav.dashboard', to: { name: 'dashboard' } }, { label: 'nav.media' }])
  })

  it('collection-list includes the group then the collection label', () => {
    const c = buildBreadcrumb({ name: 'collection-list', params: { name: 'article' } }, collections, t)
    expect(c).toEqual([
      { label: 'nav.dashboard', to: { name: 'dashboard' } },
      { label: 'Content' },
      { label: 'Article' },
    ])
  })

  it('ungrouped collection omits the group crumb', () => {
    const c = buildBreadcrumb({ name: 'collection-list', params: { name: 'page' } }, collections, t)
    expect(c).toEqual([
      { label: 'nav.dashboard', to: { name: 'dashboard' } },
      { label: 'Page' },
    ])
  })

  it('collection-create appends a generic new-item leaf and links the collection', () => {
    const c = buildBreadcrumb({ name: 'collection-create', params: { name: 'article' } }, collections, t)
    expect(c).toEqual([
      { label: 'nav.dashboard', to: { name: 'dashboard' } },
      { label: 'Content' },
      { label: 'Article', to: { name: 'collection-list', params: { name: 'article' } } },
      { label: 'breadcrumb.newItem' },
    ])
  })

  it('collection-item appends a generic edit-item leaf', () => {
    const c = buildBreadcrumb({ name: 'collection-item', params: { name: 'article', id: 'x' } }, collections, t)
    expect(c[c.length - 1]).toEqual({ label: 'breadcrumb.editItem' })
    expect(c[2]).toEqual({ label: 'Article', to: { name: 'collection-list', params: { name: 'article' } } })
  })

  it('unknown collection falls back to the raw name', () => {
    const c = buildBreadcrumb({ name: 'collection-list', params: { name: 'ghost' } }, collections, t)
    expect(c).toEqual([{ label: 'nav.dashboard', to: { name: 'dashboard' } }, { label: 'ghost' }])
  })
})
