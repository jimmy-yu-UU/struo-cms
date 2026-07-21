import { describe, it, expect } from 'vitest'
import { formatRevisionTime } from './formatRevisionTime'

describe('formatRevisionTime', () => {
  it('formats a valid ISO timestamp using locale conventions', () => {
    expect(formatRevisionTime('2026-07-21T10:00:00Z')).toBe(new Date('2026-07-21T10:00:00Z').toLocaleString())
  })

  it('returns the raw string when it is not a valid date', () => {
    expect(formatRevisionTime('not-a-date')).toBe('not-a-date')
  })

  it('returns an em dash when the value is empty', () => {
    expect(formatRevisionTime('')).toBe('—')
  })
})
