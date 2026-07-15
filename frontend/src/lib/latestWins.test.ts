import { describe, it, expect } from 'vitest'
import { createLatestWins } from './latestWins'

describe('createLatestWins', () => {
  it('hands out strictly increasing tokens', () => {
    const lw = createLatestWins()
    const a = lw.next()
    const b = lw.next()
    const c = lw.next()
    expect(b).toBeGreaterThan(a)
    expect(c).toBeGreaterThan(b)
  })

  it('only the most recently issued token is current', () => {
    const lw = createLatestWins()
    const first = lw.next()
    const second = lw.next()
    expect(lw.isCurrent(first)).toBe(false)
    expect(lw.isCurrent(second)).toBe(true)
  })

  it('a freshly issued token is current until the next one is issued', () => {
    const lw = createLatestWins()
    const t = lw.next()
    expect(lw.isCurrent(t)).toBe(true)
    lw.next()
    expect(lw.isCurrent(t)).toBe(false)
  })

  it('independent instances do not share state', () => {
    const a = createLatestWins()
    const b = createLatestWins()
    const ta = a.next()
    b.next()
    b.next()
    expect(a.isCurrent(ta)).toBe(true)
  })
})
