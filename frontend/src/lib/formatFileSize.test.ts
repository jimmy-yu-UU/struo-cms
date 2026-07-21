import { describe, it, expect } from 'vitest'
import { formatFileSize } from './formatFileSize'

describe('formatFileSize', () => {
  it('formats bytes under 1 KiB as B', () => {
    expect(formatFileSize(0)).toBe('0 B')
    expect(formatFileSize(512)).toBe('512 B')
  })
  it('formats KiB with one decimal', () => {
    expect(formatFileSize(1024)).toBe('1.0 KB')
    expect(formatFileSize(1536)).toBe('1.5 KB')
  })
  it('formats MB and GB', () => {
    expect(formatFileSize(1048576)).toBe('1.0 MB')
    expect(formatFileSize(1073741824)).toBe('1.0 GB')
  })
  it('returns empty string for invalid input', () => {
    expect(formatFileSize(-1)).toBe('')
    expect(formatFileSize(Number.NaN)).toBe('')
  })
})
