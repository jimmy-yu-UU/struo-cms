import { describe, it, expect } from 'vitest'
import { splitFields } from './splitFields'
import type { CollectionMeta, FieldMeta } from '../types/schema'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta = (fields: FieldMeta[]): CollectionMeta => ({ name: 'article', label: 'Article', fields })

describe('splitFields', () => {
  it('partitions by translatable, excludes system, orders by sort', () => {
    const m = meta([
      field('id', { isSystem: true, sort: 0 }),
      field('body', { translatable: true, sort: 2 }),
      field('status', { sort: 1 }),
      field('title', { translatable: true, sort: 1 }),
    ])
    const { shared, translatable } = splitFields(m)
    expect(shared.map((f) => f.name)).toEqual(['status'])
    expect(translatable.map((f) => f.name)).toEqual(['title', 'body'])
  })
})
