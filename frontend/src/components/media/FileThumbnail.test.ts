import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'

const image: FileRow = { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 10 }
const pdf: FileRow = { id: 'f2', fileName: 'a.pdf', contentType: 'application/pdf', size: 10 }

describe('FileThumbnail', () => {
  it('defaults to the tile size', () => {
    const w = mount(FileThumbnail, { props: { file: image } })
    expect(w.get('.file-thumb').attributes('data-size')).toBe('tile')
  })

  it('honours the sm size', () => {
    const w = mount(FileThumbnail, { props: { file: image, size: 'sm' } })
    expect(w.get('.file-thumb').attributes('data-size')).toBe('sm')
  })

  // A prop assertion made only against the initial render can't distinguish a real prop binding
  // from local state seeded once at mount -- confirm the rendered attribute keeps following the
  // prop after it changes post-mount too.
  it('follows the size prop after it changes post-mount', async () => {
    const w = mount(FileThumbnail, { props: { file: image, size: 'tile' } })
    expect(w.get('.file-thumb').attributes('data-size')).toBe('tile')
    await w.setProps({ size: 'sm' })
    expect(w.get('.file-thumb').attributes('data-size')).toBe('sm')
  })

  it('renders an <img> for an image content type', () => {
    expect(mount(FileThumbnail, { props: { file: image } }).find('img').exists()).toBe(true)
  })

  it('falls back to a chip with a resolved lucide glyph when the image fails to load', async () => {
    const w = mount(FileThumbnail, { props: { file: image } })
    await w.find('img').trigger('error')
    expect(w.find('img').exists()).toBe(false)
    expect(w.find('.file-chip').exists()).toBe(true)
    expect(w.find('.lucide-image').exists()).toBe(true)
    expect(w.find('.pi').exists()).toBe(false)
    expect(w.find('.file-chip__meta').text()).toBe('PNG')
  })

  it('renders a resolved lucide glyph for a non-previewable type, not a primeicons class', () => {
    const w = mount(FileThumbnail, { props: { file: pdf } })
    expect(w.find('.file-chip').exists()).toBe(true)
    // lib/fileTypeDisplay maps application/pdf to the pi-file-pdf token, which ICON_MAP resolves
    // to lucide's FileType. Asserting the resolved icon proves the token went through resolveIcon
    // rather than being rendered as a primeicons font class.
    expect(w.find('.lucide-file-type').exists()).toBe(true)
    expect(w.find('.pi').exists()).toBe(false)
    expect(w.text()).toContain('PDF')
  })
})
