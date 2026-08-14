import { describe, it, expect } from 'vitest'
import { registry, getFieldType } from './registry'
import { ALL_FIELD_INTERFACES } from './types'

describe('inline layout hint', () => {
  it('marks the two boolean interfaces inline', () => {
    expect(getFieldType('boolean').inline).toBe(true)
    expect(getFieldType('checkbox').inline).toBe(true)
  })

  it('leaves every other interface non-inline', () => {
    const inline = ALL_FIELD_INTERFACES.filter((i) => registry[i].inline === true)
    expect(inline.sort()).toEqual(['boolean', 'checkbox'])
  })

  it('does not mark the unknown-interface fallback inline', () => {
    expect(getFieldType('not-a-real-interface').inline).not.toBe(true)
  })
})
