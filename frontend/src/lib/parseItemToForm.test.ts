import { describe, it, expect } from 'vitest'
import { parseItemToForm, blankItemForm } from './parseItemToForm'
import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', relations: [], fields: [
  field('id', { isSystem: true }),
  field('status', { sort: 1 }),
  field('title', { translatable: true, sort: 2 }),
]}
const locales: LanguageInfo[] = [
  { code: 'en', name: 'English', isDefault: true },
  { code: 'zh-TW', name: '繁中', isDefault: false },
]

describe('parseItemToForm', () => {
  it('inflates shared + per-locale translations, seeding missing locales', () => {
    const item = { id: '1', status: 'published', translations: { en: { title: 'Hello' } } }
    const model = parseItemToForm(meta, item, locales)
    expect(model.shared).toEqual({ status: 'published' })
    expect(model.translations.en).toEqual({ title: 'Hello' })
    expect(model.translations['zh-TW']).toEqual({ title: '' })
  })
})

describe('blankItemForm', () => {
  it('seeds empty shared + empty per-locale entries', () => {
    const model = blankItemForm(meta, locales)
    expect(model.shared).toEqual({ status: '' })
    expect(model.translations.en).toEqual({ title: '' })
    expect(model.translations['zh-TW']).toEqual({ title: '' })
  })
})
