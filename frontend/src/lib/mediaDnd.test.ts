import { describe, it, expect, vi } from 'vitest'
import { setDragPayload, isMediaDrag, readDragPayload } from './mediaDnd'
import { DRAG_MIME, serializeMovePayload } from './mediaMove'

function fakeDataTransfer(overrides: Partial<{ types: string[]; getData: (t: string) => string }> = {}) {
  return {
    types: overrides.types ?? [],
    getData: overrides.getData ?? vi.fn(() => ''),
    setData: vi.fn(),
    effectAllowed: '',
  }
}

describe('setDragPayload', () => {
  it('writes the serialized payload under DRAG_MIME', () => {
    const dataTransfer = fakeDataTransfer()
    const ev = { dataTransfer } as unknown as DragEvent
    setDragPayload(ev, { files: ['f1'], folders: [] })
    expect(dataTransfer.setData).toHaveBeenCalledWith(DRAG_MIME, serializeMovePayload({ files: ['f1'], folders: [] }))
  })

  it('sets effectAllowed to move', () => {
    const dataTransfer = fakeDataTransfer()
    const ev = { dataTransfer } as unknown as DragEvent
    setDragPayload(ev, { files: [], folders: ['d1'] })
    expect(dataTransfer.effectAllowed).toBe('move')
  })
})

describe('isMediaDrag', () => {
  it('is false for a foreign drag (e.g. text/plain)', () => {
    const ev = { dataTransfer: fakeDataTransfer({ types: ['text/plain'] }) } as unknown as DragEvent
    expect(isMediaDrag(ev)).toBe(false)
  })

  it('is true when the drag carries the media MIME type', () => {
    const ev = { dataTransfer: fakeDataTransfer({ types: [DRAG_MIME] }) } as unknown as DragEvent
    expect(isMediaDrag(ev)).toBe(true)
  })

  it('is false when dataTransfer is absent', () => {
    const ev = { dataTransfer: null } as unknown as DragEvent
    expect(isMediaDrag(ev)).toBe(false)
  })
})

describe('readDragPayload', () => {
  it('returns null for a foreign drop', () => {
    const ev = { dataTransfer: fakeDataTransfer({ types: ['text/plain'], getData: () => 'hello' }) } as unknown as DragEvent
    expect(readDragPayload(ev)).toBeNull()
  })

  it('returns the parsed payload for a media drop', () => {
    const payload = { files: ['f1'], folders: ['d1'] }
    const ev = {
      dataTransfer: fakeDataTransfer({
        types: [DRAG_MIME],
        getData: (t: string) => (t === DRAG_MIME ? serializeMovePayload(payload) : ''),
      }),
    } as unknown as DragEvent
    expect(readDragPayload(ev)).toEqual(payload)
  })
})
