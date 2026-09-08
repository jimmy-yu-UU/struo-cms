import { describe, it, expect } from 'vitest'
import { buildItemPayload } from './buildItemPayload'
import { emptyLink, visiblePayloadFields } from './junctionLinks'
import type { CollectionMeta, FieldMeta, RelationMeta, LanguageInfo } from '../types/schema'
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

describe('buildItemPayload file/image fields', () => {
  const fileMeta: CollectionMeta = { name: 'article', label: 'Article', relations: [], fields: [
    field('id', { isSystem: true }),
    field('heroImageId', { interface: 'file' }),
  ]}
  const langs: LanguageInfo[] = [{ code: 'en', name: 'English', isDefault: true }]

  it('serializes an empty file/image value as null (not "") on update, to avoid a backend Guid.Parse("") 500', () => {
    const model: FormModel = { shared: { heroImageId: '' }, translations: {}, relations: {} }
    const p = buildItemPayload(fileMeta, model, langs, 'update')
    expect(p.heroImageId).toBeNull()
  })

  it('round-trips a set file/image value unchanged on update', () => {
    const model: FormModel = { shared: { heroImageId: 'file-123' }, translations: {}, relations: {} }
    const p = buildItemPayload(fileMeta, model, langs, 'update')
    expect(p.heroImageId).toBe('file-123')
  })
})

describe('buildItemPayload date/time/dateTime fields', () => {
  const dtMeta: CollectionMeta = { name: 'article', label: 'Article', relations: [], fields: [
    field('id', { isSystem: true }),
    field('publishedAt', { interface: 'dateTime' }),
  ]}
  const langs: LanguageInfo[] = [{ code: 'en', name: 'English', isDefault: true }]

  it('serializes an empty dateTime value as null (not "") on update, to avoid a backend DateTime? parse 400', () => {
    const model: FormModel = { shared: { publishedAt: '' }, translations: {}, relations: {} }
    const p = buildItemPayload(dtMeta, model, langs, 'update')
    expect('publishedAt' in p).toBe(true)
    expect(p.publishedAt).toBeNull()
  })

  it('round-trips a set dateTime value unchanged on update', () => {
    const model: FormModel = { shared: { publishedAt: '2026-01-02T03:04:05Z' }, translations: {}, relations: {} }
    const p = buildItemPayload(dtMeta, model, langs, 'update')
    expect(p.publishedAt).toBe('2026-01-02T03:04:05Z')
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
  it('echoes version on update but not on create', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: {}, relations: {}, version: 3 }
    const upd = buildItemPayload(meta, model, locales, 'update')
    expect(upd.version).toBe(3)
    const created = buildItemPayload(meta, model, locales, 'create')
    expect(created).not.toHaveProperty('version')
  })
  it('omits version on update when the model has none', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: {}, relations: {} }
    const upd = buildItemPayload(meta, model, locales, 'update')
    expect(upd).not.toHaveProperty('version')
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

describe('buildItemPayload junction links', () => {
  const tagsRel: RelationMeta = { name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag', interface: 'tagSelect',
    foreignKey: null, displayTemplate: '{Name}', editable: true, selfReferencing: false,
    junctionCollection: 'articleTag', junctionPayloadFields: ['note', 'weight', 'secret'], sortField: 'sort' }
  const rolesRel: RelationMeta = { ...tagsRel, name: 'roles', label: 'Roles', targetCollection: 'role',
    junctionCollection: 'userRole', junctionPayloadFields: null, sortField: null }
  const articleMeta: CollectionMeta = { name: 'article', label: 'Article', fields: [field('status')], relations: [tagsRel, rolesRel] }
  const junctionMeta: CollectionMeta = { name: 'articleTag', label: 'Article tag', relations: [], fields: [
    field('articleId', { interface: 'uuid' }), field('tagId', { interface: 'uuid' }),
    field('note', { sort: 1, maxLength: 5, required: true }), field('weight', { interface: 'number', sort: 2 }),
    field('secret', { hidden: true }), field('sort', { interface: 'number' }),
  ] }
  const resolve = (n: string) => (n === 'articleTag' ? junctionMeta : undefined)
  const junctionLocales: LanguageInfo[] = [{ code: 'en', name: 'English', isDefault: true }]

  const model: FormModel = { shared: { status: 'draft' }, translations: {}, version: 3,
    relations: { tags: [{ id: 't1', junction: { note: 'x', weight: 2 } }, { id: 't2', junction: { note: '', weight: null } }], roles: ['r1'] } }
  it('sends {id, ...payload} objects in order when the junction is writable', () => {
    const p = buildItemPayload(articleMeta, model, junctionLocales, 'update', { resolveCollection: resolve, canWriteJunction: () => true })
    expect(p.tags).toEqual([{ id: 't1', note: 'x', weight: 2 }, { id: 't2', note: '', weight: null }])
    expect(p.roles).toEqual(['r1'])
  })
  it('sends bare ids when the junction is not writable, and without options', () => {
    expect(buildItemPayload(articleMeta, model, junctionLocales, 'update', { resolveCollection: resolve, canWriteJunction: () => false }).tags).toEqual(['t1', 't2'])
    expect(buildItemPayload(articleMeta, model, junctionLocales, 'update').tags).toEqual(['t1', 't2'])
  })
  it('sends bare ids for a sortField-only relation even when writable (the server rejects objects there)', () => {
    const sortOnly: CollectionMeta = { ...articleMeta, relations: [{ ...tagsRel, junctionPayloadFields: null }] }
    const m: FormModel = { ...model, relations: { tags: [{ id: 't2', junction: {} }, { id: 't1', junction: {} }] } }
    expect(buildItemPayload(sortOnly, m, junctionLocales, 'update', { resolveCollection: resolve, canWriteJunction: () => true }).tags).toEqual(['t2', 't1'])
  })
  it('a freshly-added link (emptyLink defaults) serializes its numeric field as null, not "" (spec finding #1: '
    + 'a blank number would otherwise 400 the whole save)', () => {
    const link = emptyLink('t3', visiblePayloadFields(tagsRel, resolve))
    expect(link.junction).toEqual({ note: '', weight: '' }) // registry default, pre-serialize
    const m: FormModel = { ...model, relations: { tags: [link], roles: [] } }
    const p = buildItemPayload(articleMeta, m, junctionLocales, 'update', { resolveCollection: resolve, canWriteJunction: () => true })
    expect(p.tags).toEqual([{ id: 't3', note: '', weight: null }])
  })
})
