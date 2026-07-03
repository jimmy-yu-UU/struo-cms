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
})
