import { describe, it, expect } from 'vitest'
import { loadedNullsOf, nullsAfterSave } from './loadedNulls'
import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', relations: [], fields: [
  field('id', { isSystem: true }),
  field('status', { sort: 1 }),
  field('note', { sort: 2 }),
  field('title', { translatable: true, sort: 3 }),
  field('summary', { translatable: true, sort: 4 }),
]}
const locales: LanguageInfo[] = [
  { code: 'en', name: 'English', isDefault: true },
  { code: 'zh-TW', name: '繁中', isDefault: false },
]

describe('loadedNullsOf', () => {
  it('lists shared and translation fields the item carried as null', () => {
    const item = { id: '1', status: null, note: 'x', translations: { en: { title: 'Hi', summary: null } } }
    expect(loadedNullsOf(meta, item, locales)).toEqual({ shared: ['status'], translations: { en: ['summary'] } })
  })
  it('ignores absent keys and an item with no values', () => {
    expect(loadedNullsOf(meta, {}, locales)).toEqual({ shared: [], translations: {} })
  })
})

describe('nullsAfterSave', () => {
  const prev = { shared: ['status'], translations: { en: ['summary'] } }
  it('drops a field saved with a value and keeps one saved as null', () => {
    const payload = { status: 'draft', note: null, translations: { en: { title: 'Hi', summary: null } } }
    expect(nullsAfterSave(meta, prev, payload, locales)).toEqual({
      shared: ['note'], translations: { en: ['summary'] },
    })
  })
  it('marks every translatable field of a locale deleted with null', () => {
    const payload = { status: null, translations: { 'zh-TW': null } }
    expect(nullsAfterSave(meta, prev, payload, locales)).toEqual({
      shared: ['status'], translations: { en: ['summary'], 'zh-TW': ['title', 'summary'] },
    })
  })
  it('keeps a locale the payload did not send', () => {
    expect(nullsAfterSave(meta, prev, { status: '' }, locales).translations).toEqual({ en: ['summary'] })
  })
})
