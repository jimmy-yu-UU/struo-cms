import { describe, it, expect } from 'vitest'
import { buildItemPayload } from './buildItemPayload'
import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', relations: [], fields: [
  field('id', { isSystem: true }),
  field('status'),
  field('title', { translatable: true }),
]}
const locales: LanguageInfo[] = [
  { code: 'en', name: 'English', isDefault: true },
  { code: 'zh-TW', name: '繁中', isDefault: false },
]

describe('buildItemPayload (create)', () => {
  it('puts shared at top level and always includes default locale; omits empty non-default', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } } }
    const p = buildItemPayload(meta, model, locales, 'create')
    expect(p.status).toBe('draft')
    expect(p.translations).toEqual({ en: { title: 'Hi' } })
  })
  it('includes a non-default locale when it has content', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '嗨' } } }
    const p = buildItemPayload(meta, model, locales, 'create')
    expect(p.translations).toEqual({ en: { title: 'Hi' }, 'zh-TW': { title: '嗨' } })
  })
  it('omits empty optional shared fields on create', () => {
    const model: FormModel = { shared: { status: '' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } } }
    const p = buildItemPayload(meta, model, locales, 'create')
    expect('status' in p).toBe(false)
  })
})

describe('buildItemPayload (update)', () => {
  it('keeps shared keys (partial) and only touched locales', () => {
    const model: FormModel = { shared: { status: 'published' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } } }
    const p = buildItemPayload(meta, model, locales, 'update')
    expect(p.status).toBe('published')
    expect(p.translations).toEqual({ en: { title: 'Hi' } })
  })
})
