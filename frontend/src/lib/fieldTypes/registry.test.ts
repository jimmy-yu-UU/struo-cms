import { describe, it, expect } from 'vitest'
import { getFieldType } from './registry'
import { ALL_FIELD_INTERFACES } from './types'
import type { FieldMeta } from '../../types/schema'
import { formatCell } from '../formatCell'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}

const LIST_ELIGIBLE = new Set([
  'text', 'textarea', 'slug', 'email', 'url', 'phone', 'color',
  'number', 'slider', 'rating', 'boolean', 'checkbox',
  'date', 'time', 'dateTime', 'select', 'radio',
])

describe('field-type registry', () => {
  it('resolves a def for every known interface', () => {
    for (const i of ALL_FIELD_INTERFACES) expect(getFieldType(i).component).toBeTruthy()
  })

  it('falls back to the read-only def for unknown interfaces', () => {
    expect(getFieldType('somethingNew').component).toBe(getFieldType('json').component)
    expect(getFieldType('somethingNew').listColumn).toBeNull()
  })

  it('lists exactly the legacy scalar interfaces as list columns', () => {
    for (const i of ALL_FIELD_INTERFACES) {
      const eligible = getFieldType(i).listColumn !== null
      expect(eligible, i).toBe(LIST_ELIGIBLE.has(i))
    }
  })

  it('defaults file/image to null and everything else to empty string', () => {
    const f = field({ interface: 'file' })
    expect(getFieldType('file').defaultValue(f)).toBeNull()
    expect(getFieldType('image').defaultValue(f)).toBeNull()
    expect(getFieldType('text').defaultValue(field({ interface: 'text' }))).toBe('')
    expect(getFieldType('richText').defaultValue(field({ interface: 'richText' }))).toBe('')
  })

  it('parse returns the raw value or the default when nullish', () => {
    expect(getFieldType('text').parse('hi', field({ interface: 'text' }))).toBe('hi')
    expect(getFieldType('text').parse(undefined, field({ interface: 'text' }))).toBe('')
    expect(getFieldType('file').parse(undefined, field({ interface: 'file' }))).toBeNull()
  })

  it('serialize coerces empty string to null only for file/image', () => {
    expect(getFieldType('file').serialize('', field({ interface: 'file' }))).toBeNull()
    expect(getFieldType('image').serialize('', field({ interface: 'image' }))).toBeNull()
    expect(getFieldType('file').serialize('id1', field({ interface: 'file' }))).toBe('id1')
    expect(getFieldType('text').serialize('', field({ interface: 'text' }))).toBe('')
  })

  it('list formatters match the legacy formatCell output', () => {
    const sel = field({ interface: 'select', options: [{ value: 'draft', label: 'Draft' }] })
    expect(getFieldType('select').listColumn!.format('draft', sel)).toBe(formatCell('draft', sel))
    const bool = field({ interface: 'boolean' })
    expect(getFieldType('boolean').listColumn!.format(true, bool)).toBe('Yes')
    const dt = field({ interface: 'dateTime' })
    expect(getFieldType('dateTime').listColumn!.format('2026-01-02T03:04:05Z', dt)).toContain('2026')
    expect(getFieldType('dateTime').listColumn!.format('not-a-date', dt)).toBe('not-a-date')
  })
})
