import { describe, it, expect } from 'vitest'
import { relatedListFilterField } from './relatedListFilter'
import type { CollectionMeta, RelationMeta } from '../types/schema'

const relation: RelationMeta = {
  name: 'kids', label: 'Kids', kind: 'oneToMany', targetCollection: 'child', interface: 'relatedList',
  foreignKey: 'parentId', editable: false, selfReferencing: false,
}

function back(name: string, over: Partial<RelationMeta> = {}): RelationMeta {
  return {
    name, label: name, kind: 'manyToOne', targetCollection: 'parent', interface: 'dropdown',
    foreignKey: 'parentId', editable: true, selfReferencing: false, ...over,
  }
}

function child(relations: RelationMeta[]): CollectionMeta {
  return { name: 'child', label: 'Child', fields: [], relations }
}

describe('relatedListFilterField', () => {
  it('filters through the relation back to the parent when the child declares one', () => {
    expect(relatedListFilterField(relation, child([back('parent')]), 'parent')).toBe('parent.id')
  })

  it('filters by the key when the child declares no relation back', () => {
    expect(relatedListFilterField(relation, child([]), 'parent')).toBe('parentId')
  })

  it('filters by the key when the child collection is not loaded or the parent is not known', () => {
    expect(relatedListFilterField(relation, undefined, 'parent')).toBe('parentId')
    expect(relatedListFilterField(relation, child([back('parent')]), undefined)).toBe('parentId')
  })

  it('compares the key ignoring case and spells the key in camelCase', () => {
    const upper = { ...relation, foreignKey: 'ParentId' }
    expect(relatedListFilterField(upper, child([back('parent', { foreignKey: 'parentid' })]), 'parent')).toBe('parent.id')
    expect(relatedListFilterField(upper, child([]), 'parent')).toBe('parentId')
  })

  it('ignores a relation on another key, to another collection, or of another kind', () => {
    const others = [
      back('byOther', { foreignKey: 'otherId' }),
      back('toElsewhere', { targetCollection: 'elsewhere' }),
      back('many', { kind: 'manyToMany' }),
    ]
    expect(relatedListFilterField(relation, child(others), 'parent')).toBe('parentId')
  })

  it('takes the first of several candidates in declaration order', () => {
    expect(relatedListFilterField(relation, child([back('first'), back('second')]), 'parent')).toBe('first.id')
  })

  it('returns an empty field when the relation has no foreign key', () => {
    expect(relatedListFilterField({ ...relation, foreignKey: null }, child([]), 'parent')).toBe('')
  })
})
