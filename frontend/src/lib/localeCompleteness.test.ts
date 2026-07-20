import { describe, it, expect } from 'vitest'
import { hasLocaleContent } from './localeCompleteness'
import type { FieldMeta } from '../types/schema'

function f(name: string): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: true, sort: 0, isSystem: false }
}
const fields = [f('title'), f('body')]

describe('hasLocaleContent', () => {
  it('is false when all fields are missing or empty', () => {
    expect(hasLocaleContent(fields, {})).toBe(false)
    expect(hasLocaleContent(fields, { title: '', body: undefined })).toBe(false)
  })
  it('is false for whitespace-only strings and empty arrays', () => {
    expect(hasLocaleContent(fields, { title: '   ', body: [] })).toBe(false)
  })
  it('is true when any field has a non-empty string', () => {
    expect(hasLocaleContent(fields, { title: 'Hello', body: '' })).toBe(true)
  })
  it('counts numbers (incl. 0), booleans (incl. false), and non-empty arrays as content', () => {
    expect(hasLocaleContent([f('n')], { n: 0 })).toBe(true)
    expect(hasLocaleContent([f('b')], { b: false })).toBe(true)
    expect(hasLocaleContent([f('tags')], { tags: ['x'] })).toBe(true)
  })
  it('is false when there are no translatable fields', () => {
    expect(hasLocaleContent([], { anything: 'x' })).toBe(false)
  })
})
