import { describe, it, expect } from 'vitest'
import { resolveItemTitle } from './resolveItemTitle'
import type { CollectionMeta, FieldMeta } from '../types/schema'

function field(over: Partial<FieldMeta> = {}): FieldMeta {
  return { name: 'title', label: 'Title', interface: 'text', required: false, searchable: false,
    sortable: false, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false, ...over }
}
function meta(over: Partial<CollectionMeta> = {}): CollectionMeta {
  return { name: 't', label: 'T', fields: [], relations: [], defaultDisplayField: null, ...over }
}

describe('resolveItemTitle', () => {
  it('reads a non-translatable defaultDisplayField', () => {
    const m = meta({ defaultDisplayField: 'title', fields: [field()] })
    expect(resolveItemTitle({ id: '1', title: 'Hello' }, m, 'en')).toBe('Hello')
  })
  it('reads a translatable defaultDisplayField from translations[locale]', () => {
    const m = meta({ defaultDisplayField: 'title', fields: [field({ translatable: true })] })
    const row = { id: '2', translations: { en: { title: 'Hi' }, 'zh-TW': { title: '嗨' } } }
    expect(resolveItemTitle(row, m, 'zh-TW')).toBe('嗨')
  })
  it('falls back to id when no display field resolves', () => {
    expect(resolveItemTitle({ id: '3' }, meta(), 'en')).toBe('3')
  })
  it('falls back to empty string when there is no id', () => {
    expect(resolveItemTitle({}, meta(), 'en')).toBe('')
  })
})
