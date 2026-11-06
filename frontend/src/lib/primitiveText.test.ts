import { describe, it, expect } from 'vitest'
import { primitiveText } from './primitiveText'

describe('primitiveText', () => {
  it('returns strings as they are', () => {
    expect(primitiveText('a b')).toBe('a b')
    expect(primitiveText('')).toBe('')
  })

  it('prints numbers, booleans and bigints', () => {
    expect(primitiveText(0)).toBe('0')
    expect(primitiveText(12.5)).toBe('12.5')
    expect(primitiveText(false)).toBe('false')
    expect(primitiveText(10n)).toBe('10')
  })

  it('reads null, undefined, objects, arrays and functions as empty', () => {
    expect(primitiveText(null)).toBe('')
    expect(primitiveText(undefined)).toBe('')
    expect(primitiveText({ a: 1 })).toBe('')
    expect(primitiveText([1, 2])).toBe('')
    expect(primitiveText(() => 1)).toBe('')
  })
})
