import { describe, it, expect } from 'vitest'
import { formatDateTime } from './formatDateTime'

describe('formatDateTime', () => {
  it('formats as YYYY-MM-DD HH:mm in local time', () => {
    const d = new Date(2026, 6, 14, 18, 32) // local 2026-07-14 18:32 narrative-guard:allow: documents the Date object's local value under test, not a changelog date
    expect(formatDateTime(d.toISOString())).toBe('2026-07-14 18:32')
  })
  it('returns em-dash for null/empty/invalid', () => {
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime('')).toBe('—')
    expect(formatDateTime('not-a-date')).toBe('—')
  })
})
