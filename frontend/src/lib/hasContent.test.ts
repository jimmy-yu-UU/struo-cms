import { describe, it, expect } from 'vitest'
import { hasContent, anyHasContent } from './hasContent'
import type { FieldMeta } from '../types/schema'

function f(name: string): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: true, sort: 0, isSystem: false }
}

describe('hasContent', () => {
  it.each([
    ['undefined', undefined],
    ['null', null],
    ['empty string', ''],
    ['whitespace', ' \t\n '],
    ['empty array', []],
    ['empty object', {}],
  ])('treats %s as no content', (_n, v) => {
    expect(hasContent(v)).toBe(false)
  })

  it.each([
    ['zero', 0],
    ['false', false],
    ['true', true],
    ['number', 3],
    ['text', 'x'],
    ['non-empty array', ['a']],
    ['array of empty strings', ['']],
    ['object with a key', { k: '' }],
    ['rich text with text', '<p>hi</p>'],
    ['rich text with image only', '<p><img src="/a.png"></p>'],
    ['rich text with a rule', '<hr>'],
    ['heading with text', '<h2>t</h2>'],
  ])('treats %s as content', (_n, v) => {
    expect(hasContent(v)).toBe(true)
  })
})

const RICH = { interface: 'richText' }

describe('hasContent for a rich text field', () => {
  it.each([
    ['empty paragraph', '<p></p>'],
    ['paragraph with br', '<p><br></p>'],
    ['self-closed br', '<p><br/></p>'],
    ['several empty paragraphs', '<p></p><p><br></p><p> </p>'],
    ['paragraph with attributes', '<p style="text-align: center"></p>'],
    ['paragraph with nbsp', '<p>&nbsp;</p>'],
    ['empty heading', '<h2></h2>'],
    ['nested empty list', '<ul><li><p></p></li></ul>'],
  ])('treats %s as no content', (_n, v) => {
    expect(hasContent(v, RICH)).toBe(false)
  })
  it.each([
    ['text', '<p>hi</p>'],
    ['image only', '<p><img src="/a.png"></p>'],
    ['rule', '<hr>'],
    ['heading with text', '<h2>t</h2>'],
  ])('treats %s as content', (_n, v) => {
    expect(hasContent(v, RICH)).toBe(true)
  })
})

describe('hasContent for a plain text field', () => {
  it.each(['<TBD>', '<b></b>', '<p></p>', '<br>'])('treats %s as content', (v) => {
    expect(hasContent(v)).toBe(true)
    expect(hasContent(v, { interface: 'text' })).toBe(true)
  })
  it('treats whitespace as no content', () => {
    expect(hasContent('  ', { interface: 'text' })).toBe(false)
  })
})

describe('anyHasContent', () => {
  it('is field-aware: markup-only counts as empty for rich text and as content for text', () => {
    const rich = { ...f('b'), interface: 'richText' } as FieldMeta
    expect(anyHasContent([rich], { b: '<h2></h2>' })).toBe(false)
    expect(anyHasContent([f('a')], { a: '<TBD>' })).toBe(true)
  })
  it('is true when any field has content', () => {
    expect(anyHasContent([f('a'), f('b')], { a: [], b: 0 })).toBe(true)
  })
  it('is false when every field is empty or there are no fields', () => {
    expect(anyHasContent([f('a'), f('b')], { a: [], b: {} })).toBe(false)
    expect(anyHasContent([], { a: 'x' })).toBe(false)
  })
})

describe('hasContent on pathological input', () => {
  it('handles a long run of "<" without a closing ">" in linear time and treats it as content', () => {
    const run = '<'.repeat(50_000)
    const start = performance.now()
    expect(hasContent(run, { interface: 'richText' })).toBe(true)
    expect(hasContent(run)).toBe(true)
    expect(performance.now() - start).toBeLessThan(500)
  })
})
