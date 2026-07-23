import { describe, it, expect } from 'vitest'
import { toFileRow, toFileRows } from './toFileRow'

describe('toFileRow', () => {
  it('maps a full raw row to a FileRow', () => {
    const raw = {
      id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024,
      width: 800, height: 600, status: 'published', createdAt: '2026-07-01T00:00:00Z',
      extra: 'ignored',
    }
    expect(toFileRow(raw)).toEqual({
      id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024,
      width: 800, height: 600, status: 'published', createdAt: '2026-07-01T00:00:00Z',
    })
  })

  it('defaults missing required fields and leaves optional fields undefined', () => {
    const raw = { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }
    const row = toFileRow(raw)
    expect(row).toEqual({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1,
      width: undefined, height: undefined, status: undefined, createdAt: undefined })
  })

  it('coerces malformed field types to safe defaults instead of propagating them', () => {
    const raw = { id: 42, fileName: null, contentType: undefined, size: 'big', width: 'x', height: null }
    expect(toFileRow(raw)).toEqual({
      id: '', fileName: '', contentType: '', size: 0, width: undefined, height: null,
      status: undefined, createdAt: undefined,
    })
  })

  it('maps an array of raw rows', () => {
    const raw = [
      { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 },
      { id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2 },
    ]
    expect(toFileRows(raw).map((r) => r.id)).toEqual(['f1', 'f2'])
  })
})
