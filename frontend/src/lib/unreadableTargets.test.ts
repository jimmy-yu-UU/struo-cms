import { describe, it, expect } from 'vitest'
import { isFileFieldUnreadable, isRelationTargetUnreadable } from './unreadableTargets'
import type { FieldMeta, RelationMeta } from '../types/schema'

const field = (over: Partial<FieldMeta>): FieldMeta => ({ name: 'f', label: 'f', interface: 'image', required: false,
  searchable: false, sortable: false, readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over })
const rel = (over: Partial<RelationMeta>): RelationMeta => ({ name: 'r', label: 'r', kind: 'manyToOne', targetCollection: 'category',
  interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '', editable: true, selfReferencing: false, ...over })
const deny = (name: string) => (n: string) => n !== name

describe('isRelationTargetUnreadable', () => {
  it('is true for a written relation whose target is denied', () => {
    expect(isRelationTargetUnreadable(rel({}), deny('category'))).toBe(true)
    expect(isRelationTargetUnreadable(rel({ interface: 'tagSelect' }), deny('category'))).toBe(true)
    expect(isRelationTargetUnreadable(rel({ interface: 'treeSelect' }), deny('category'))).toBe(true)
  })
  it('is false when the target is readable, without a check, or for a relation the form never writes', () => {
    expect(isRelationTargetUnreadable(rel({}), deny('tag'))).toBe(false)
    expect(isRelationTargetUnreadable(rel({}))).toBe(false)
    expect(isRelationTargetUnreadable(rel({ interface: 'relatedList' }), deny('category'))).toBe(false)
  })
})

describe('isFileFieldUnreadable', () => {
  it('is true for a shared file, image or files field when the file collection is denied', () => {
    for (const interfaceName of ['file', 'image', 'files']) {
      expect(isFileFieldUnreadable(field({ interface: interfaceName as FieldMeta['interface'] }), deny('file'))).toBe(true)
    }
  })
  it('is false for a translatable field, another interface, a readable collection or no check', () => {
    expect(isFileFieldUnreadable(field({ translatable: true }), deny('file'))).toBe(false)
    expect(isFileFieldUnreadable(field({ interface: 'text' }), deny('file'))).toBe(false)
    expect(isFileFieldUnreadable(field({}), deny('tag'))).toBe(false)
    expect(isFileFieldUnreadable(field({}))).toBe(false)
  })
})
