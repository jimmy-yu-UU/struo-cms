import { describe, it, expect } from 'vitest'
import { resolveGreetingKey } from './greeting'

describe('resolveGreetingKey', () => {
  it('morning before noon', () => {
    expect(resolveGreetingKey(0)).toBe('morning')
    expect(resolveGreetingKey(11)).toBe('morning')
  })
  it('afternoon from 12 to before 18', () => {
    expect(resolveGreetingKey(12)).toBe('afternoon')
    expect(resolveGreetingKey(17)).toBe('afternoon')
  })
  it('evening from 18 onward', () => {
    expect(resolveGreetingKey(18)).toBe('evening')
    expect(resolveGreetingKey(23)).toBe('evening')
  })
})
