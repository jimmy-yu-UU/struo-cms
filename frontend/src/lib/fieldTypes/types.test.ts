import { describe, it, expect } from 'vitest'
import { isEmpty, ALL_FIELD_INTERFACES } from './types'

describe('fieldTypes/types', () => {
  it('isEmpty treats null, undefined and empty string as empty', () => {
    expect(isEmpty(null)).toBe(true)
    expect(isEmpty(undefined)).toBe(true)
    expect(isEmpty('')).toBe(true)
    expect(isEmpty(0)).toBe(false)
    expect(isEmpty('x')).toBe(false)
    expect(isEmpty(false)).toBe(false)
  })

  it('lists all 33 backend field interfaces', () => {
    expect(ALL_FIELD_INTERFACES).toHaveLength(33)
    expect(new Set(ALL_FIELD_INTERFACES).size).toBe(33)
    expect(ALL_FIELD_INTERFACES).toContain('richText')
    expect(ALL_FIELD_INTERFACES).toContain('dateTime')
  })
})
