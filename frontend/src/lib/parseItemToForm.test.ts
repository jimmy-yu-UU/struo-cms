import { describe, it, expect } from 'vitest'
import { parseItemToForm, blankItemForm } from './parseItemToForm'
import type { CollectionMeta, FieldMeta, RelationMeta, LanguageInfo } from '../types/schema'

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

describe('parseItemToForm file/image fields', () => {
  const fileMeta: CollectionMeta = { name: 'article', label: 'Article', relations: [], fields: [
    field('id', { isSystem: true }),
    field('heroImageId', { interface: 'file' }),
  ]}

  it('seeds a missing file/image value as null (not "") so it survives unchanged through an update', () => {
    const model = parseItemToForm(fileMeta, {}, locales)
    expect(model.shared.heroImageId).toBeNull()
  })

  it('carries a set file/image value through unchanged', () => {
    const model = parseItemToForm(fileMeta, { heroImageId: 'file-1' }, locales)
    expect(model.shared.heroImageId).toBe('file-1')
  })
})

describe('parseItemToForm relations', () => {
  const langs: LanguageInfo[] = [{ code: 'en', name: 'English', isDefault: true }]
  function relMeta(): CollectionMeta {
    return {
      name: 'article', label: 'Article', fields: [], defaultDisplayField: null,
      relations: [
        { name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false },
        { name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag', interface: 'tagSelect', foreignKey: null, displayTemplate: '{Name}', editable: true, selfReferencing: false },
      ],
    }
  }
  it('inflates M2O id and M2M id array from deep-expanded item', () => {
    const item = { id: '1', category: { id: 'cat-1', name: 'Tech' }, tags: [{ id: 't1' }, { id: 't2' }] }
    const model = parseItemToForm(relMeta(), item, langs)
    expect(model.relations.category).toBe('cat-1')
    expect(model.relations.tags).toEqual(['t1', 't2'])
  })
  it('blank form seeds M2O null and M2M empty array', () => {
    const model = blankItemForm(relMeta(), langs)
    expect(model.relations.category).toBeNull()
    expect(model.relations.tags).toEqual([])
  })
})

describe('parseItemToForm junction links', () => {
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

  const item = { id: 'a1', status: 'draft', version: 3,
    tags: [
      { id: 't1', name: 'One', _junction: { note: 'hello', weight: 2 } },
      { id: 't2', name: 'Two' }, // no _junction (e.g. no read grant) -> empty junction
    ],
    roles: [{ id: 'r1' }] }
  it('maps payload relations to RelationLink[] when a resolver is given', () => {
    const m = parseItemToForm(articleMeta, item, junctionLocales, resolve)
    expect(m.relations.tags).toEqual([
      { id: 't1', junction: { note: 'hello', weight: 2 } },
      { id: 't2', junction: { note: '', weight: '' } }, // number.parse(undefined) -> '' per registry.ts, not null
    ])
  })
  it('keeps id[] for relations without payload or sortField, and without a resolver', () => {
    expect(parseItemToForm(articleMeta, item, junctionLocales, resolve).relations.roles).toEqual(['r1'])
    expect(parseItemToForm(articleMeta, item, junctionLocales).relations.tags).toEqual(['t1', 't2'])
  })
  it('blankItemForm yields an empty link array for payload relations', () => {
    expect(blankItemForm(articleMeta, junctionLocales, resolve).relations.tags).toEqual([])
  })
})
