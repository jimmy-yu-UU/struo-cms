import { describe, it, expect } from 'vitest'
import { formatClock } from './formatClock'

describe('formatClock', () => {
  it('formats under an hour as m:ss', () => {
    expect(formatClock(0)).toBe('0:00')
    expect(formatClock(5)).toBe('0:05')
    expect(formatClock(599)).toBe('9:59')
    expect(formatClock(1800)).toBe('30:00')
    expect(formatClock(3599)).toBe('59:59')
  })

  it('formats an hour or more as h:mm:ss', () => {
    expect(formatClock(3600)).toBe('1:00:00')
    expect(formatClock(3725)).toBe('1:02:05')
    expect(formatClock(36000)).toBe('10:00:00')
  })

  it('clamps negative and fractional input', () => {
    expect(formatClock(-4)).toBe('0:00')
    expect(formatClock(61.9)).toBe('1:01')
  })
})
