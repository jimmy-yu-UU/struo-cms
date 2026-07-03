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
    const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } }, relations: {} }
    const p = buildItemPayload(meta, model, locales, 'create')
    expect(p.status).toBe('draft')
    expect(p.translations).toEqual({ en: { title: 'Hi' } })
  })
  it('includes a non-default locale when it has content', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '嗨' } }, relations: {} }
    const p = buildItemPayload(meta, model, locales, 'create')
    expect(p.translations).toEqual({ en: { title: 'Hi' }, 'zh-TW': { title: '嗨' } })
  })
  it('omits empty optional shared fields on create', () => {
    const model: FormModel = { shared: { status: '' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } }, relations: {} }
    const p = buildItemPayload(meta, model, locales, 'create')
    expect('status' in p).toBe(false)
  })
})

describe('buildItemPayload (update)', () => {
  it('keeps shared keys (partial) and only touched locales', () => {
    const model: FormModel = { shared: { status: 'published' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } }, relations: {} }
    const p = buildItemPayload(meta, model, locales, 'update')
    expect(p.status).toBe('published')
    expect(p.translations).toEqual({ en: { title: 'Hi' } })
  })
})

describe('buildItemPayload relations', () => {
  it('writes M2O FK (camelCased) and M2M id array; omits relatedList and untouched relations', () => {
    const model: FormModel = {
      shared: {}, translations: {},
      relations: { category: 'cat-1', tags: ['t1', 't2'] }, // comments untouched
    }
    const meta: CollectionMeta = {
      name: 'article', label: 'Article', fields: [], defaultDisplayField: null,
      relations: [
        { name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false },
        { name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag', interface: 'tagSelect', foreignKey: null, displayTemplate: '{Name}', editable: true, selfReferencing: false },
        { name: 'comments', label: 'Comments', kind: 'oneToMany', targetCollection: 'comment', interface: 'relatedList', foreignKey: 'ArticleId', displayTemplate: '{Body}', editable: false, selfReferencing: false },
      ],
    }
    const langs: LanguageInfo[] = [{ code: 'en', name: 'English', isDefault: true }]
    const payload = buildItemPayload(meta, model, langs, 'update')
    expect(payload.categoryId).toBe('cat-1')
    expect(payload.tags).toEqual(['t1', 't2'])
    expect(payload).not.toHaveProperty('comments')
  })
  it('sends empty M2M array to clear, and null FK to clear', () => {
    const meta: CollectionMeta = {
      name: 'article', label: 'Article', fields: [], defaultDisplayField: null,
      relations: [
        { name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false },
        { name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag', interface: 'tagSelect', foreignKey: null, displayTemplate: '{Name}', editable: true, selfReferencing: false },
      ],
    }
    const langs: LanguageInfo[] = [{ code: 'en', name: 'English', isDefault: true }]
    const model: FormModel = { shared: {}, translations: {}, relations: { category: null, tags: [] } }
    const payload = buildItemPayload(meta, model, langs, 'update')
    expect(payload.categoryId).toBeNull()
    expect(payload.tags).toEqual([])
  })
})
