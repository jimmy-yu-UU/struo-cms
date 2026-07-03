import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import FileThumbnail from './FileThumbnail.vue'

const img = { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024 }
const doc = { id: 'f2', fileName: 'a.pdf', contentType: 'application/pdf', size: 2048 }

describe('FileThumbnail', () => {
  it('renders an img for image content types', () => {
    const w = mount(FileThumbnail, { props: { file: img } })
    const el = w.find('img')
    expect(el.exists()).toBe(true)
    expect(el.attributes('src')).toMatch(/\/files\/f1\/content$/)
  })

  it('renders a chip (no img) for non-image content types', () => {
    const w = mount(FileThumbnail, { props: { file: doc } })
    expect(w.find('img').exists()).toBe(false)
    expect(w.text()).toContain('a.pdf')
  })

  it('falls back to chip when the image fails to load', async () => {
    const w = mount(FileThumbnail, { props: { file: img } })
    await w.find('img').trigger('error')
    expect(w.find('img').exists()).toBe(false)
    expect(w.text()).toContain('a.png')
  })
})
