import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'

const image: FileRow = { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024 }
const pdf: FileRow = { id: 'f2', fileName: 'a.pdf', contentType: 'application/pdf', size: 2048 }

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

  it('renders an img for image content types, wired to the real content URL', () => {
    const w = mount(FileThumbnail, { props: { file: image } })
    const el = w.find('img')
    expect(el.exists()).toBe(true)
    expect(el.attributes('src')).toMatch(/\/files\/f1\/content$/)
  })

  // Every browser makes an <img> draggable by default, and that native drag's `dragstart` bubbles
  // up into any ancestor tile/card wired for media drag-and-drop (MediaGrid) -- an explicit
  // draggable="false" stops the browser's own image-drag affordance so it can never fire, as
  // defence in depth alongside the permission check in the ancestor's own dragstart handler.
  it('marks the img non-draggable so it cannot originate its own native drag', () => {
    const w = mount(FileThumbnail, { props: { file: image } })
    expect(w.find('img').attributes('draggable')).toBe('false')
  })

  it('renders a chip (no img) for non-image types: a resolved lucide glyph + short label, not the raw MIME, filename, or a primeicons class', () => {
    const w = mount(FileThumbnail, { props: { file: pdf } })
    expect(w.find('img').exists()).toBe(false)
    // lib/fileTypeDisplay maps application/pdf to the pi-file-pdf token, which ICON_MAP resolves
    // to lucide's FileType. Asserting the resolved icon proves the token went through resolveIcon
    // rather than being rendered as a primeicons font class.
    expect(w.find('.file-chip__icon.lucide-file-type').exists()).toBe(true)
    expect(w.find('.pi').exists()).toBe(false)
    expect(w.find('.file-chip__meta').text()).toBe('PDF')
    // the long/ugly MIME string and the filename must NOT appear (the tile caption shows the name)
    expect(w.text()).not.toContain('application/pdf')
    expect(w.text()).not.toContain('a.pdf')
  })

  it('falls back to chip with a resolved lucide glyph when the image fails to load', async () => {
    const w = mount(FileThumbnail, { props: { file: image } })
    await w.find('img').trigger('error')
    expect(w.find('img').exists()).toBe(false)
    expect(w.find('.file-chip__icon.lucide-image').exists()).toBe(true)
    expect(w.find('.pi').exists()).toBe(false)
    expect(w.find('.file-chip__meta').text()).toBe('PNG')
    expect(w.text()).not.toContain('image/png')
    expect(w.text()).not.toContain('a.png')
  })
})
