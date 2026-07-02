import { describe, it, expect } from 'vitest'
import { selectListColumns } from './selectListColumns'
import type { CollectionMeta, FieldMeta } from '../types/schema'

function field(partial: Partial<FieldMeta> & { name: string; interface: string }): FieldMeta {
  return {
    label: partial.name, required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
    ...partial,
  } as FieldMeta
}

function meta(fields: FieldMeta[], defaultDisplayField?: string): CollectionMeta {
  return { name: 'article', label: 'Article', defaultDisplayField, fields }
}

describe('selectListColumns', () => {
  it('puts DefaultDisplayField first, keeps eligible scalars, carries sortable', () => {
    const cols = selectListColumns(meta([
      field({ name: 'title', label: 'Title', interface: 'text', sortable: true }),
      field({ name: 'status', label: 'Status', interface: 'select' }),
    ], 'status'))
    expect(cols.map((c) => c.field)).toEqual(['status', 'title'])
    expect(cols.find((c) => c.field === 'title')!.sortable).toBe(true)
    expect(cols.find((c) => c.field === 'status')!.header).toBe('Status')
  })

  it('excludes system, hidden, richText, relations and file/image interfaces', () => {
    const cols = selectListColumns(meta([
      field({ name: 'title', interface: 'text' }),
      field({ name: 'body', interface: 'richText' }),
      field({ name: 'cover', interface: 'image' }),
      field({ name: 'attachment', interface: 'file' }),
      field({ name: 'secret', interface: 'text', hidden: true }),
      field({ name: 'id', interface: 'uuid', isSystem: true }),
    ]))
    expect(cols.map((c) => c.field)).toEqual(['title'])
  })

  it('caps at 6 columns', () => {
    const many = Array.from({ length: 10 }, (_, i) => field({ name: `f${i}`, interface: 'text' }))
    expect(selectListColumns(meta(many))).toHaveLength(6)
  })
})
