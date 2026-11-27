import { describe, it, expect } from 'vitest'
import { fieldsLeftByRevert, isFileFieldUnreadable, isRelationTargetUnreadable } from './unreadableTargets'
import type { CollectionMeta, FieldMeta, RelationMeta } from '../types/schema'

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
  it('is true for a shared or translatable file, image or files field when the file collection is denied', () => {
    for (const interfaceName of ['file', 'image', 'files']) {
      for (const translatable of [false, true]) {
        const f = field({ interface: interfaceName as FieldMeta['interface'], translatable })
        expect(isFileFieldUnreadable(f, deny('file'))).toBe(true)
      }
    }
  })
  it('is false for another interface, a readable collection or no check', () => {
    expect(isFileFieldUnreadable(field({ translatable: true }), deny('tag'))).toBe(false)
    expect(isFileFieldUnreadable(field({ translatable: true }))).toBe(false)
    expect(isFileFieldUnreadable(field({ interface: 'text', translatable: true }), deny('file'))).toBe(false)
    expect(isFileFieldUnreadable(field({ interface: 'text' }), deny('file'))).toBe(false)
    expect(isFileFieldUnreadable(field({}), deny('tag'))).toBe(false)
    expect(isFileFieldUnreadable(field({}))).toBe(false)
  })
})

describe('fieldsLeftByRevert', () => {
  const meta = (fields: FieldMeta[], relations: RelationMeta[]): CollectionMeta => ({ name: 'article', label: 'Article', fields, relations })
  const article = meta(
    [
      field({ name: 'title', label: 'Title', interface: 'text' }),
      field({ name: 'hero', label: 'Hero image' }),
      field({ name: 'gallery', label: 'Gallery', interface: 'files' }),
      field({ name: 'cover', label: 'Cover', translatable: true }),
      field({ name: 'stamp', label: 'Stamp', readOnly: true }),
      field({ name: 'sys', label: 'System image', isSystem: true }),
    ],
    [
      rel({ name: 'category', label: 'Category' }),
      rel({ name: 'tags', label: 'Tags', kind: 'manyToMany', interface: 'tagSelect', targetCollection: 'tag', foreignKey: null }),
      rel({ name: 'comments', label: 'Comments', kind: 'oneToMany', interface: 'relatedList', targetCollection: 'comment' }),
    ],
  )

  it('is empty for a user who reads everything, and without a check', () => {
    expect(fieldsLeftByRevert(article, () => true)).toEqual([])
    expect(fieldsLeftByRevert(article)).toEqual([])
  })
  it('lists the labels of a many-to-one relation whose target cannot be read', () => {
    expect(fieldsLeftByRevert(article, deny('category'))).toEqual(['Category'])
  })
  it('lists the labels of a many-to-many relation whose target cannot be read', () => {
    expect(fieldsLeftByRevert(article, deny('tag'))).toEqual(['Tags'])
  })
  it('lists every shared and translatable file field when file cannot be read, not read-only or system ones', () => {
    expect(fieldsLeftByRevert(article, deny('file'))).toEqual(['Hero image', 'Gallery', 'Cover'])
  })
  it('lists fields before relations, labels and not keys', () => {
    const left = fieldsLeftByRevert(article, (n) => n === 'comment')
    expect(left).toEqual(['Hero image', 'Gallery', 'Cover', 'Category', 'Tags'])
    expect(left).not.toContain('hero')
  })
})
