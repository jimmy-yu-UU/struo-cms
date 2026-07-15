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
