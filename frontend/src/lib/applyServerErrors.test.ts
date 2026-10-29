import { describe, it, expect } from 'vitest'
import { splitServerErrors } from './applyServerErrors'

const known: ReadonlySet<string> = new Set(['title', 'status'])

describe('splitServerErrors', () => {
  it('maps a matching field detail into fieldErrors', () => {
    const r = splitServerErrors([{ field: 'title', message: 'dup' }], known)
    expect(r.fieldErrors).toEqual({ title: 'dup' })
    expect(r.leftover).toEqual([])
  })

  it('matches case-insensitively but writes back the canonical field name', () => {
    // ASP.NET ModelState keys do not preserve declared casing; the field error
    // must key on the meta's canonical name so the form can render it.
    const r = splitServerErrors([{ field: 'Title', message: 'dup' }], known)
    expect(r.fieldErrors).toEqual({ title: 'dup' })
    expect(r.leftover).toEqual([])
  })

  it('routes unknown fields to leftover', () => {
    const r = splitServerErrors([{ field: 'mystery', message: 'nope' }], known)
    expect(r.fieldErrors).toEqual({})
    expect(r.leftover).toEqual(['nope'])
  })

  it('splits a mix of known and unknown fields', () => {
    const r = splitServerErrors(
      [{ field: 'status', message: 'bad' }, { field: 'mystery', message: 'nope' }],
      known,
    )
    expect(r.fieldErrors).toEqual({ status: 'bad' })
    expect(r.leftover).toEqual(['nope'])
  })

  it('keeps only the first message when a field repeats (one-message-per-field)', () => {
    const r = splitServerErrors(
      [{ field: 'title', message: 'first' }, { field: 'Title', message: 'second' }],
      known,
    )
    expect(r.fieldErrors).toEqual({ title: 'first' })
    expect(r.leftover).toEqual([])
  })

  it('handles a field canonically named like an Object.prototype key', () => {
    // Guards against `canonical in fieldErrors` consulting the prototype chain: a field named
    // 'constructor' would otherwise be seen as already-present and silently dropped.
    const r = splitServerErrors(
      [{ field: 'constructor', message: 'reserved-name error' }],
      new Set(['constructor']),
    )
    expect(r.fieldErrors).toEqual({ constructor: 'reserved-name error' })
    expect(r.leftover).toEqual([])
  })

  it('returns empty results for undefined details', () => {
    const r = splitServerErrors(undefined, known)
    expect(r.fieldErrors).toEqual({})
    expect(r.leftover).toEqual([])
  })

  it('returns empty results for empty details', () => {
    const r = splitServerErrors([], known)
    expect(r.fieldErrors).toEqual({})
    expect(r.leftover).toEqual([])
  })
})

describe('splitServerErrors with locale', () => {
  it('maps a detail carrying a locale to that locale key', () => {
    const r = splitServerErrors([{ field: 'title', message: 'req', locale: 'zh-TW' }], known,
      { defaultLocale: 'en', locales: new Set(['en', 'zh-TW']) })
    expect(r.fieldErrors).toEqual({ 'zh-TW:title': 'req' })
    expect(r.leftover).toEqual([])
  })
  it('maps a default-locale detail to the bare field key', () => {
    const r = splitServerErrors([{ field: 'Title', message: 'req', locale: 'en' }], known,
      { defaultLocale: 'en', locales: new Set(['en', 'zh-TW']) })
    expect(r.fieldErrors).toEqual({ title: 'req' })
  })
  it('keeps one message per locale and field, separate across locales', () => {
    const r = splitServerErrors([
      { field: 'title', message: 'a', locale: 'fr' }, { field: 'title', message: 'b', locale: 'fr' },
      { field: 'title', message: 'c', locale: 'de' },
    ], known, { defaultLocale: 'en', locales: new Set(['en', 'fr', 'de']) })
    expect(r.fieldErrors).toEqual({ 'fr:title': 'a', 'de:title': 'c' })
  })
  it('routes a detail for an unknown locale to leftover', () => {
    const r = splitServerErrors([{ field: 'title', message: 'req', locale: 'xx' }], known,
      { defaultLocale: 'en', locales: new Set(['en']) })
    expect(r.fieldErrors).toEqual({})
    expect(r.leftover).toEqual(['req'])
  })
  it('leaves details without a locale on the bare key', () => {
    const r = splitServerErrors([{ field: 'title', message: 'dup' }], known,
      { defaultLocale: 'en', locales: new Set(['en', 'zh-TW']) })
    expect(r.fieldErrors).toEqual({ title: 'dup' })
  })
})
