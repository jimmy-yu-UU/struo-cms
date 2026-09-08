import { describe, it, expect } from 'vitest'
import {
  visiblePayloadFields, usesLinksEditor, canReadJunction, canWriteJunction, emptyLink, isRelationLinks,
  junctionAccessFrom, type JunctionAccess, type JunctionAccessSource,
} from './junctionLinks'
import type { CollectionMeta, FieldMeta, RelationMeta } from '../types/schema'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const junction: CollectionMeta = {
  name: 'articleTag', label: 'Article tag', hidden: true, relations: [],
  fields: [
    field('articleId', { interface: 'uuid', sort: 1 }),
    field('tagId', { interface: 'uuid', sort: 2 }),
    field('weight', { interface: 'number', sort: 4 }),
    field('note', { sort: 3, maxLength: 200 }),
    field('secret', { hidden: true, sort: 5 }),
    field('sort', { interface: 'number', sort: 6 }),
  ],
}
const adminOnlyJunction: CollectionMeta = { ...junction, name: 'userRole', adminOnly: true }
const resolve = (n: string) => ({ articleTag: junction, userRole: adminOnlyJunction } as Record<string, CollectionMeta>)[n]

function rel(over: Partial<RelationMeta> = {}): RelationMeta {
  return { name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag', interface: 'tagSelect',
    foreignKey: null, displayTemplate: '{Name}', editable: true, selfReferencing: false,
    junctionCollection: 'articleTag', junctionPayloadFields: ['note', 'weight', 'secret'], sortField: 'sort', ...over }
}
const access = (over: Partial<JunctionAccess> = {}): JunctionAccess => ({
  canRead: () => true, canWrite: () => true, isSuperAdmin: false, ...over,
})

describe('visiblePayloadFields', () => {
  it('returns the listed junction fields minus hidden ones, in field sort order', () => {
    expect(visiblePayloadFields(rel(), resolve).map((f) => f.name)).toEqual(['note', 'weight'])
  })
  it('is empty without a junction collection, without a list, or when the junction is unknown', () => {
    expect(visiblePayloadFields(rel({ junctionCollection: null, junctionPayloadFields: null }), resolve)).toEqual([])
    expect(visiblePayloadFields(rel({ junctionPayloadFields: [] }), resolve)).toEqual([])
    expect(visiblePayloadFields(rel(), () => undefined)).toEqual([])
  })
})

describe('usesLinksEditor', () => {
  it('is true for a tagSelect with visible payload fields', () => {
    expect(usesLinksEditor(rel({ sortField: null }), resolve)).toBe(true)
  })
  it('is true for a tagSelect with only a sortField', () => {
    expect(usesLinksEditor(rel({ junctionPayloadFields: null }), resolve)).toBe(true)
  })
  it('is false with neither, and false for non-tagSelect interfaces', () => {
    expect(usesLinksEditor(rel({ junctionPayloadFields: null, sortField: null }), resolve)).toBe(false)
    expect(usesLinksEditor(rel({ junctionPayloadFields: ['secret'] , sortField: null }), resolve)).toBe(false) // hidden-only
    expect(usesLinksEditor(rel({ interface: 'dropdown', kind: 'manyToOne' }), resolve)).toBe(false)
  })
})

describe('canReadJunction / canWriteJunction', () => {
  it('needs a junction collection and the read grant', () => {
    expect(canReadJunction(rel(), access())).toBe(true)
    expect(canReadJunction(rel({ junctionCollection: null }), access())).toBe(false)
    expect(canReadJunction(rel(), access({ canRead: () => false }))).toBe(false)
  })
  it('write requires read + write grant', () => {
    expect(canWriteJunction(rel(), resolve, access())).toBe(true)
    expect(canWriteJunction(rel(), resolve, access({ canRead: () => false }))).toBe(false)
    expect(canWriteJunction(rel(), resolve, access({ canWrite: () => false }))).toBe(false)
  })
  it('an adminOnly junction additionally requires super-admin', () => {
    const r = rel({ junctionCollection: 'userRole' })
    expect(canWriteJunction(r, resolve, access())).toBe(false)
    expect(canWriteJunction(r, resolve, access({ isSuperAdmin: true }))).toBe(true)
  })
})

describe('emptyLink / isRelationLinks', () => {
  it('seeds every visible payload field with its registry default', () => {
    const link = emptyLink('t1', visiblePayloadFields(rel(), resolve))
    expect(link.id).toBe('t1')
    expect(Object.keys(link.junction).sort()).toEqual(['note', 'weight'])
  })
  it('recognises link arrays and rejects id arrays', () => {
    expect(isRelationLinks([{ id: 'a', junction: {} }])).toBe(true)
    expect(isRelationLinks([])).toBe(true)
    expect(isRelationLinks(['a', 'b'])).toBe(false)
    expect(isRelationLinks(null)).toBe(false)
  })
  it('rejects elements with a non-string id or a missing/null junction', () => {
    expect(isRelationLinks([{ id: 1, junction: {} }])).toBe(false)
    expect(isRelationLinks([{ id: 'a' }])).toBe(false)
    expect(isRelationLinks([{ id: 'a', junction: null }])).toBe(false)
  })
})

describe('junctionAccessFrom (finding #3: single adapter shared by ItemFormView and JunctionLinksEditor)', () => {
  function source(over: Partial<JunctionAccessSource> = {}): JunctionAccessSource {
    return { canRead: () => true, canWrite: () => true, user: { isSuperAdmin: false }, ...over }
  }
  it('delegates canRead/canWrite straight to the source, by collection name', () => {
    const seenRead: string[] = []
    const seenWrite: string[] = []
    const a = junctionAccessFrom(source({
      canRead: (c) => { seenRead.push(c); return true }, canWrite: (c) => { seenWrite.push(c); return false },
    }))
    expect(a.canRead('articleTag')).toBe(true)
    expect(a.canWrite('articleTag')).toBe(false)
    expect(seenRead).toEqual(['articleTag'])
    expect(seenWrite).toEqual(['articleTag'])
  })
  it('isSuperAdmin is true only for a logged-in super-admin user', () => {
    expect(junctionAccessFrom(source({ user: { isSuperAdmin: true } })).isSuperAdmin).toBe(true)
    expect(junctionAccessFrom(source({ user: { isSuperAdmin: false } })).isSuperAdmin).toBe(false)
  })
  it('isSuperAdmin is false with no logged-in user', () => {
    expect(junctionAccessFrom(source({ user: null })).isSuperAdmin).toBe(false)
  })
})
