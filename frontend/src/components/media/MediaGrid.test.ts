import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import MediaGrid from './MediaGrid.vue'
import { DRAG_MIME, serializeMovePayload } from '../../lib/mediaMove'

const files = [
  { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 },
  { id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2 },
]

describe('MediaGrid', () => {
  it('renders one tile per file', () => {
    const w = mount(MediaGrid, { props: { files } })
    expect(w.findAll('.media-tile')).toHaveLength(2)
  })

  it('emits select with the file id on click when selectable', async () => {
    const w = mount(MediaGrid, { props: { files, selectable: true } })
    await w.findAll('.media-tile')[1].trigger('click')
    expect(w.emitted('select')?.[0]).toEqual(['f2'])
  })

  it('marks the selected tile', () => {
    const w = mount(MediaGrid, { props: { files, selectable: true, selectedId: 'f2' } })
    expect(w.findAll('.media-tile')[1].classes()).toContain('is-selected')
  })

  it('emits toggle with the id on click in multiple mode', async () => {
    const w = mount(MediaGrid, { props: { files, multiple: true, selectedIds: [] } })
    await w.findAll('.media-tile')[0].trigger('click')
    expect(w.emitted('toggle')?.[0]).toEqual(['f1'])
    expect(w.emitted('select')).toBeUndefined()
  })

  it('marks tiles whose id is in selectedIds (multiple mode)', () => {
    const w = mount(MediaGrid, { props: { files, multiple: true, selectedIds: ['f2'] } })
    expect(w.findAll('.media-tile')[1].classes()).toContain('is-selected')
    expect(w.findAll('.media-tile')[0].classes()).not.toContain('is-selected')
  })

  it('emits open with the id on click when not selectable', async () => {
    const w = mount(MediaGrid, { props: { files } })
    await w.findAll('.media-tile')[0].trigger('click')
    expect(w.emitted('open')?.[0]).toEqual(['f1'])
    expect(w.emitted('select')).toBeUndefined()
    expect(w.emitted('toggle')).toBeUndefined()
  })

  it('renders the actions slot once per file', () => {
    const w = mount(MediaGrid, {
      props: { files },
      slots: { actions: '<button class="act">{{ params.file.id }}</button>' },
    })
    expect(w.findAll('.act')).toHaveLength(files.length)
  })

  // Permissions: file moves require canWrite('file') -- without that grant a tile must not be a
  // drag source at all.
  it('is not draggable when canMove is false or unset', () => {
    const w = mount(MediaGrid, { props: { files } })
    expect(w.find('.media-tile-wrap').attributes('draggable')).toBeUndefined()
  })

  it('is draggable and writes the file payload on dragstart when canMove is true', async () => {
    const w = mount(MediaGrid, { props: { files, canMove: true } })
    const wrap = w.find('.media-tile-wrap')
    expect(wrap.attributes('draggable')).toBe('true')
    const setData = vi.fn()
    await wrap.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
    expect(setData).toHaveBeenCalledWith(DRAG_MIME, serializeMovePayload({ files: ['f1'], folders: [] }))
  })
})
