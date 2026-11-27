import { describe, it, expect } from 'vitest'
import { localizeCoreSchema, type CoreLabelCatalog } from './localizeCoreSchema'
import type { CollectionMeta } from '../types/schema'

const en: CoreLabelCatalog = {
  groups: { System: 'System' },
  audit: { createdAt: 'CreatedAt' },
  collections: {
    role: {
      label: 'Role',
      fields: { name: 'Name' },
      relations: { users: 'Users' },
      options: { kind: { a: 'Alpha' } },
    },
  },
}
const zh: CoreLabelCatalog = {
  groups: { System: '系統' },
  audit: { createdAt: '建立時間' },
  collections: {
    role: {
      label: '角色',
      fields: { name: '名稱' },
      relations: { users: '使用者' },
      options: { kind: { a: '甲' } },
    },
  },
}
const fr: CoreLabelCatalog = {
  groups: { System: 'Système' },
  audit: { createdAt: 'Créé le' },
  collections: { role: { label: 'Rôle', fields: { name: 'Nom' }, relations: { users: 'Utilisateurs' }, options: { kind: { a: 'Alpha-fr' } } } },
}

function field(name: string, label: string, options?: { value: string; label: string }[], isSystem = false) {
  return { name, label, interface: 'text', required: false, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem, options }
}
const auditField = (name: string, label: string) => field(name, label, undefined, true)
function role(over: Partial<CollectionMeta> = {}): CollectionMeta {
  return {
    name: 'role',
    label: 'Role',
    group: 'System',
    fields: [field('name', 'Name'), auditField('createdAt', 'CreatedAt'), field('kind', 'Kind', [{ value: 'a', label: 'Alpha' }])],
    relations: [{ name: 'users', label: 'Users', kind: 'oneToMany', targetCollection: 'user', interface: 'relatedList', editable: false, selfReferencing: false }],
    ...over,
  }
}
const toZh = (c: CollectionMeta[]) => localizeCoreSchema(c, { source: en, target: zh })

describe('localizeCoreSchema', () => {
  it('replaces every label that equals the source text', () => {
    const [out] = toZh([role()])
    expect(out.label).toBe('角色')
    expect(out.group).toBe('系統')
    expect(out.fields.map((f) => f.label)).toEqual(['名稱', '建立時間', 'Kind'])
    expect(out.fields[2].options?.[0].label).toBe('甲')
    expect(out.relations[0].label).toBe('使用者')
  })

  it('keeps a server label that differs from the source text', () => {
    const [out] = toZh([role({ label: 'Job title', group: 'Org', fields: [field('name', 'Full name')] })])
    expect(out.label).toBe('Job title')
    expect(out.group).toBe('Org')
    expect(out.fields[0].label).toBe('Full name')
  })

  it('keeps labels that have no entry', () => {
    const [out] = toZh([role({ fields: [field('extra', 'Extra')], relations: [] })])
    expect(out.fields[0].label).toBe('Extra')
  })

  it('leaves a collection outside the catalog untouched', () => {
    const article: CollectionMeta = { name: 'article', label: 'Role', group: 'Content', fields: [field('name', 'Name')], relations: [] }
    const [out] = toZh([article])
    expect(out).toEqual(article)
  })

  describe('audit fields of a collection outside the catalog', () => {
    const post = (fields: CollectionMeta['fields']): CollectionMeta => ({ name: 'post', label: 'Post', group: 'Content', fields, relations: [] })

    it('translates the audit fields of a fork collection', () => {
      const [out] = toZh([post([auditField('createdAt', 'CreatedAt'), field('title', 'Title')])])
      expect(out.fields.map((f) => f.label)).toEqual(['建立時間', 'Title'])
    })

    it('keeps an audit label the fork changed', () => {
      const [out] = toZh([post([auditField('createdAt', 'Written on')])])
      expect(out.fields[0].label).toBe('Written on')
    })

    it('keeps a non-system field named like an audit key', () => {
      const input = post([field('createdAt', 'CreatedAt')])
      const [out] = toZh([input])
      expect(out).toBe(input)
    })

    it('does not mutate a fork collection', () => {
      const input = [post([auditField('createdAt', 'CreatedAt')])]
      const snapshot = structuredClone(input)
      const [out] = toZh(input)
      expect(input).toEqual(snapshot)
      expect(out.fields[0]).not.toBe(input[0].fields[0])
    })
  })

  it('does not mutate its input', () => {
    const input = [role()]
    const snapshot = structuredClone(input)
    const [out] = toZh(input)
    expect(input).toEqual(snapshot)
    expect(out).not.toBe(input[0])
    expect(out.fields).not.toBe(input[0].fields)
  })

  it('returns a collection named like a prototype key as it came', () => {
    const input: CollectionMeta[] = [{ name: 'constructor', label: 'constructor', fields: [field('toString', 'toString')], relations: [] }]
    const [out] = toZh(input)
    expect(out).toBe(input[0])
  })

  it('gives the other text when the target catalog changes', () => {
    const input = [role()]
    expect(toZh(input)[0].label).toBe('角色')
    const [out] = localizeCoreSchema(input, { source: en, target: fr })
    expect(out.label).toBe('Rôle')
    expect(out.fields[1].label).toBe('Créé le')
  })

  it('shows the server text when the target has no value for a key', () => {
    const sparse: CoreLabelCatalog = { groups: {}, audit: {}, collections: { role: { label: '', fields: {} } } }
    const [out] = localizeCoreSchema([role()], { source: en, target: sparse })
    expect(out.label).toBe('Role')
    expect(out.fields[0].label).toBe('Name')
  })
})
