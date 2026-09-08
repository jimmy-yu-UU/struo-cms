import { describe, it, expect } from 'vitest'
import { validateItem } from './validateItem'
import type { CollectionMeta, FieldMeta, RelationMeta } from '../types/schema'
import type { FormModel } from '../types/itemForm'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', fields: [
  field('status', { required: true }),
  field('title', { translatable: true, required: true }),
], relations: [] }

describe('validateItem', () => {
  it('flags empty required shared and default-locale required translatable', () => {
    const model: FormModel = { shared: { status: '' }, translations: { en: { title: '' }, 'zh-TW': { title: '' } }, relations: {} }
    const errs = validateItem(meta, model, 'en')
    expect(errs.status).toMatch(/required/i)
    expect(errs.title).toMatch(/required/i)
  })
  it('does not block on missing non-default locale', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } }, relations: {} }
    expect(validateItem(meta, model, 'en')).toEqual({})
  })
  it('rejects over-long shared values and accepts exactly-at-limit', () => {
    const lenMeta: CollectionMeta = { name: 'article', label: 'Article', fields: [
      field('name', { label: 'Name', maxLength: 5 }),
    ], relations: [] }
    const tooLongModel: FormModel = { shared: { name: 'x'.repeat(6) }, translations: {}, relations: {} }
    expect(validateItem(lenMeta, tooLongModel, 'en')).toEqual({ name: 'Name must be at most 5 characters.' })
    const atLimitModel: FormModel = { shared: { name: 'x'.repeat(5) }, translations: {}, relations: {} }
    expect(validateItem(lenMeta, atLimitModel, 'en')).toEqual({})
  })
  it('rejects over-long default-locale translatable values', () => {
    const lenMeta: CollectionMeta = { name: 'article', label: 'Article', fields: [
      field('title', { label: 'Title', translatable: true, maxLength: 5 }),
    ], relations: [] }
    const model: FormModel = { shared: {}, translations: { en: { title: 'x'.repeat(6) } }, relations: {} }
    expect(validateItem(lenMeta, model, 'en')).toEqual({ title: 'Title must be at most 5 characters.' })
  })
})

describe('validateItem junction links', () => {
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

  const base: FormModel = { shared: { status: 'draft' }, translations: {}, relations: {} }
  it('reports the first required/maxLength violation with its row number under the relation name', () => {
    const m = { ...base, relations: { tags: [{ id: 't1', junction: { note: 'ok', weight: 1 } }, { id: 't2', junction: { note: '', weight: 1 } }] } }
    expect(validateItem(articleMeta, m, 'en', resolve)).toEqual({ tags: 'Tags › note is required (row 2).' })
    const long = { ...base, relations: { tags: [{ id: 't1', junction: { note: 'toolong', weight: 1 } }] } }
    expect(validateItem(articleMeta, long, 'en', resolve)).toEqual({ tags: 'Tags › note must be at most 5 characters (row 1).' })
  })
  it('passes valid links and ignores id[] relations and calls without a resolver', () => {
    const ok = { ...base, relations: { tags: [{ id: 't1', junction: { note: 'ok', weight: null } }], roles: ['r1'] } }
    expect(validateItem(articleMeta, ok, 'en', resolve)).toEqual({})
    const bad = { ...base, relations: { tags: [{ id: 't1', junction: { note: '' } }] } }
    expect(validateItem(articleMeta, bad, 'en')).toEqual({})
  })

  describe('canWriteJunction (finding #2: no unsaveable form for a relation the user cannot write)', () => {
    const requiredEmpty = { ...base, relations: { tags: [{ id: 't1', junction: { note: '', weight: 1 } }] } }
    it('skips payload validation for a relation whose junction the caller cannot write', () => {
      expect(validateItem(articleMeta, requiredEmpty, 'en', resolve, () => false)).toEqual({})
    })
    it('still validates a relation the caller can write, unchanged from the no-canWriteJunction case', () => {
      expect(validateItem(articleMeta, requiredEmpty, 'en', resolve, () => true))
        .toEqual({ tags: 'Tags › note is required (row 1).' })
    })
  })
})
