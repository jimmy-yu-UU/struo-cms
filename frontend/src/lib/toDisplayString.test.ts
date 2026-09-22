import { describe, it, expect } from 'vitest'
import { toDisplayString } from './toDisplayString'

describe('toDisplayString', () => {
  it('renders null and undefined as an empty string', () => {
    expect(toDisplayString(null)).toBe('')
    expect(toDisplayString(undefined)).toBe('')
  })

  it('returns a string value unchanged', () => {
    expect(toDisplayString('hello')).toBe('hello')
    expect(toDisplayString('')).toBe('')
  })

  it('renders numbers, including falsy ones', () => {
    expect(toDisplayString(0)).toBe('0')
    expect(toDisplayString(42)).toBe('42')
  })

  it('renders booleans, including false', () => {
    expect(toDisplayString(false)).toBe('false')
    expect(toDisplayString(true)).toBe('true')
  })

  it('renders bigints', () => {
    expect(toDisplayString(10n)).toBe('10')
  })

  it('renders a nested object as JSON rather than [object Object]', () => {
    expect(toDisplayString({ a: 1, b: { c: 2 } })).toBe('{"a":1,"b":{"c":2}}')
  })

  it('renders an array as JSON', () => {
    expect(toDisplayString([1, 'x', null])).toBe('[1,"x",null]')
  })

  it('renders an empty string for a value JSON.stringify itself returns undefined for', () => {
    expect(toDisplayString(() => {})).toBe('')
    expect(toDisplayString(Symbol('s'))).toBe('')
  })
})
