import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import MediaGrid from './MediaGrid.vue'

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
})
