import { describe, it, expect } from 'vitest'
import { resolveDisplayLabel } from './resolveDisplayLabel'
import type { CollectionMeta, RelationMeta } from '../types/schema'

function meta(over: Partial<CollectionMeta> = {}): CollectionMeta {
  return { name: 't', label: 'T', fields: [], relations: [], defaultDisplayField: null, ...over }
}
const rel = (over: Partial<RelationMeta> = {}): RelationMeta => ({
  name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category',
  interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}',
  editable: true, selfReferencing: false, ...over,
})

describe('resolveDisplayLabel', () => {
  it('interpolates a template against top-level (non-translatable) fields', () => {
    const target = meta({ fields: [{ name: 'name', label: 'Name', interface: 'text', required: false, searchable: true, sortable: true, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false }] })
    expect(resolveDisplayLabel({ id: '1', name: 'Tech' }, rel(), target, 'en')).toBe('Tech')
  })
  it('reads a translatable template field from translations[locale]', () => {
    const target = meta({ fields: [{ name: 'title', label: 'Title', interface: 'text', required: true, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: true, sort: 1, isSystem: false }] })
    const row = { id: '9', translations: { en: { title: 'Hello' }, 'zh-TW': { title: '哈囉' } } }
    expect(resolveDisplayLabel(row, rel({ displayTemplate: '{Title}' }), target, 'zh-TW')).toBe('哈囉')
  })
  it('falls back to defaultDisplayField then id', () => {
    const target = meta({ defaultDisplayField: 'name', fields: [{ name: 'name', label: 'Name', interface: 'text', required: false, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false }] })
    expect(resolveDisplayLabel({ id: '3', name: 'Fallback' }, rel({ displayTemplate: null }), target, 'en')).toBe('Fallback')
    expect(resolveDisplayLabel({ id: '3' }, rel({ displayTemplate: null }), meta(), 'en')).toBe('3')
  })
})
