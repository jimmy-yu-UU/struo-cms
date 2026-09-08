import { describe, it, expect } from 'vitest'
import { getFieldType, registry } from './registry'
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
  'multiSelect', 'checkboxGroup', 'tags',
  'json', 'keyValue', 'repeater',
])

describe('field-type registry', () => {
  it('resolves a def for every known interface', () => {
    for (const i of ALL_FIELD_INTERFACES) expect(getFieldType(i).component).toBeTruthy()
  })

  it('falls back to the read-only def for unknown interfaces', () => {
    expect(getFieldType('somethingNew').component).toBe(getFieldType('uuid').component)
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

  it('serialize coerces empty string to null for file/image and passes non-empty values through', () => {
    expect(getFieldType('file').serialize('', field({ interface: 'file' }))).toBeNull()
    expect(getFieldType('image').serialize('', field({ interface: 'image' }))).toBeNull()
    expect(getFieldType('file').serialize('id1', field({ interface: 'file' }))).toBe('id1')
    expect(getFieldType('text').serialize('', field({ interface: 'text' }))).toBe('')
  })

  it('serialize coerces empty string to null for non-string-backed interfaces (number/slider/rating/boolean/checkbox/uuid), leaving text-like interfaces alone', () => {
    for (const i of ['number', 'slider', 'rating', 'boolean', 'checkbox', 'uuid']) {
      expect(getFieldType(i).serialize('', field({ interface: i })), i).toBeNull()
    }
    expect(getFieldType('number').serialize(2, field({ interface: 'number' }))).toBe(2)
    expect(getFieldType('boolean').serialize(false, field({ interface: 'boolean' }))).toBe(false)
    expect(getFieldType('uuid').serialize('abc-123', field({ interface: 'uuid' }))).toBe('abc-123')
    for (const i of ['text', 'textarea', 'select', 'radio', 'hidden']) {
      expect(getFieldType(i).serialize('', field({ interface: i })), i).toBe('')
    }
  })

  it('serialize coerces empty date/time/dateTime to null and passes ISO strings through', () => {
    for (const i of ['date', 'time', 'dateTime']) {
      const f = field({ interface: i })
      expect(getFieldType(i).serialize('', f), `${i} ''`).toBeNull()
      expect(getFieldType(i).serialize(null, f), `${i} null`).toBeNull()
      expect(getFieldType(i).serialize(undefined, f), `${i} undefined`).toBeNull()
      const iso = '2026-01-02T03:04:05Z'
      expect(getFieldType(i).serialize(iso, f), `${i} iso`).toBe(iso)
    }
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

  it('multi-value defaults are empty arrays and parse coerces non-arrays', () => {
    for (const i of ['multiSelect', 'checkboxGroup', 'tags']) {
      expect(getFieldType(i).defaultValue(field({ interface: i }))).toEqual([])
      expect(getFieldType(i).parse(undefined, field({ interface: i }))).toEqual([])
      expect(getFieldType(i).parse(['a'], field({ interface: i }))).toEqual(['a'])
    }
  })

  it('option-bound serialize drops blanks and de-duplicates', () => {
    const f = field({ interface: 'multiSelect' })
    expect(getFieldType('multiSelect').serialize(['a', '', 'a', 'b'], f)).toEqual(['a', 'b'])
  })

  it('tags serialize drops blank values/labels and de-duplicates by value', () => {
    const f = field({ interface: 'tags' })
    expect(getFieldType('tags').serialize(
      [{ value: 'tech', label: ' ' }, { value: '' }, { value: 'tech', label: 'X' }, { value: 'ai', label: '人工智慧' }], f,
    )).toEqual([{ value: 'tech' }, { value: 'ai', label: '人工智慧' }])
  })

  it('list formatters join labels for option-bound and label??value for tags', () => {
    const ms = field({ interface: 'multiSelect', options: [{ value: 'apac', label: 'APAC' }, { value: 'emea', label: 'EMEA' }] })
    expect(getFieldType('multiSelect').listColumn!.format(['apac', 'emea'], ms)).toBe('APAC, EMEA')
    expect(getFieldType('multiSelect').listColumn!.format(['apac', 'zzz'], ms)).toBe('APAC, zzz') // unknown -> raw value
    const tg = field({ interface: 'tags' })
    expect(getFieldType('tags').listColumn!.format([{ value: 'tech' }, { value: 'ai', label: '人工智慧' }], tg)).toBe('tech, 人工智慧')
  })

  it('json default is null and parse coerces nullish to null', () => {
    const f = field({ interface: 'json' })
    expect(getFieldType('json').defaultValue(f)).toBeNull()
    expect(getFieldType('json').parse(undefined, f)).toBeNull()
    expect(getFieldType('json').parse({ a: 1 }, f)).toEqual({ a: 1 })
    expect(getFieldType('json').serialize({ a: 1 }, f)).toEqual({ a: 1 })
  })

  it('keyValue default is an empty object and parse coerces non-objects', () => {
    const f = field({ interface: 'keyValue' })
    expect(getFieldType('keyValue').defaultValue(f)).toEqual({})
    expect(getFieldType('keyValue').parse(undefined, f)).toEqual({})
    expect(getFieldType('keyValue').parse(['x'], f)).toEqual({})
    expect(getFieldType('keyValue').parse({ a: '1' }, f)).toEqual({ a: '1' })
  })

  it('keyValue serialize drops blank keys and keeps last-wins', () => {
    const f = field({ interface: 'keyValue' })
    expect(getFieldType('keyValue').serialize({ a: '1', '': 'x', ' ': 'y', b: '2' }, f)).toEqual({ a: '1', b: '2' })
  })

  it('list formatters: json minifies, keyValue joins k: v', () => {
    const j = field({ interface: 'json' })
    expect(getFieldType('json').listColumn!.format({ a: 1, b: [2] }, j)).toBe('{"a":1,"b":[2]}')
    const kv = field({ interface: 'keyValue' })
    expect(getFieldType('keyValue').listColumn!.format({ a: '1', b: '2' }, kv)).toBe('a: 1, b: 2')
  })

  it('files default is an empty array and parse coerces to string[]', () => {
    const f = field({ interface: 'files' })
    expect(getFieldType('files').defaultValue(f)).toEqual([])
    expect(getFieldType('files').parse(undefined, f)).toEqual([])
    expect(getFieldType('files').parse(['a', 'b'], f)).toEqual(['a', 'b'])
    expect(getFieldType('files').parse('x', f)).toEqual([])
  })

  it('files serialize drops blanks and de-duplicates keeping first (order preserved)', () => {
    const f = field({ interface: 'files' })
    expect(getFieldType('files').serialize(['a', '', ' ', 'a', 'b'], f)).toEqual(['a', 'b'])
    expect(getFieldType('files').serialize('nope', f)).toEqual([])
  })

  it('files is not list-eligible', () => {
    expect(getFieldType('files').listColumn).toBeNull()
  })
})

describe('repeaterDef', () => {
  const field = { name: 'faqs', interface: 'repeater',
    fields: [{ name: 'question', interface: 'text' }, { name: 'answer', interface: 'textarea' }] } as never

  it('defaults to an empty array', () => {
    expect(registry.repeater.defaultValue(field)).toEqual([])
  })

  it('parses non-arrays to []', () => {
    expect(registry.repeater.parse(null, field)).toEqual([])
    expect(registry.repeater.parse([{ question: 'q' }], field)).toEqual([{ question: 'q' }])
  })

  it('serialize drops fully-blank rows', () => {
    const rows = [{ question: 'keep', answer: '' }, { question: '  ', answer: '' }]
    expect(registry.repeater.serialize(rows, field)).toEqual([{ question: 'keep', answer: '' }])
  })

  it('list column shows the count', () => {
    expect(registry.repeater.listColumn?.format([{ x: 1 }, { x: 2 }], field)).toBe('2 items')
  })
})
