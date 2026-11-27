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

  it('lists a many-to-one with a foreign key and a many-to-many whatever interface shows them', () => {
    const shown = meta([], [
      rel({ name: 'category', label: 'Category', interface: 'relatedList' }),
      rel({ name: 'tags', label: 'Tags', kind: 'manyToMany', interface: 'dropdown', targetCollection: 'tag', foreignKey: null }),
    ])
    expect(fieldsLeftByRevert(shown, () => false)).toEqual(['Category', 'Tags'])
  })
  it('lists a many-to-many that is declared not editable, as the server reads no Editable', () => {
    const locked = meta([], [
      rel({ name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag', foreignKey: null, editable: false }),
    ])
    expect(fieldsLeftByRevert(locked, () => false)).toEqual(['Tags'])
  })
  it('lists a many-to-one declared not editable whose key field is writable', () => {
    const picker = meta([field({ name: 'categoryId', label: 'Category key', interface: 'text' })], [
      rel({ name: 'category', label: 'Category', editable: false }),
    ])
    expect(fieldsLeftByRevert(picker, () => false)).toEqual(['Category'])
  })
  it('leaves out a many-to-one whose key field is read-only or an audit field, in any case', () => {
    const locked = meta(
      [
        field({ name: 'ownerId', label: 'Owner key', interface: 'text', readOnly: true }),
        field({ name: 'createdBy', label: 'Created by', interface: 'text', isSystem: true }),
        field({ name: 'categoryId', label: 'Category key', interface: 'text' }),
      ],
      [
        rel({ name: 'owner', label: 'Owner', foreignKey: 'OwnerId', editable: false }),
        rel({ name: 'author', label: 'Author', foreignKey: 'CREATEDBY' }),
        rel({ name: 'category', label: 'Category' }),
      ],
    )
    expect(fieldsLeftByRevert(locked, () => false)).toEqual(['Category'])
  })
  it('leaves out a many-to-one on a soft-delete column of a soft-deletable collection', () => {
    const trashed = { ...meta([], [rel({ name: 'remover', label: 'Remover', foreignKey: 'DeletedBy' })]), softDelete: true }
    expect(fieldsLeftByRevert(trashed, () => false)).toEqual([])
    expect(fieldsLeftByRevert({ ...trashed, softDelete: false }, () => false)).toEqual(['Remover'])
  })
  it('leaves out a many-to-one without a foreign key and a one-to-many, as the server does', () => {
    const other = meta([], [
      rel({ name: 'parent', label: 'Parent', foreignKey: null }),
      rel({ name: 'comments', label: 'Comments', kind: 'oneToMany', targetCollection: 'comment' }),
    ])
    expect(fieldsLeftByRevert(other, () => false)).toEqual([])
  })
})
