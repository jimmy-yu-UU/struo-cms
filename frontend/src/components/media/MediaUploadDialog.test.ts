import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaUploadDialog from './MediaUploadDialog.vue'
import MediaUploadDropzone from './MediaUploadDropzone.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    uploadTitle: 'Upload files',
    uploadDescription: 'Drag files here or click to browse, then upload them to this folder.',
    dropzone: 'Drop files here or click to upload',
  } } },
})

// reka's own portal wrapper is itself named Teleport and collides with VTU's stub, dropping the
// dialog body; stubbing `teleport` with renderStubDefaultSlot keeps the content in the wrapper's
// own tree. No assertion here needs the content to reach document.body.
function mountDialog() {
  return mount(MediaUploadDialog, {
    props: { visible: true },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

describe('MediaUploadDialog', () => {
  it('renders the dropzone when visible', () => {
    const w = mountDialog()
    expect(w.findComponent(MediaUploadDropzone).exists()).toBe(true)
  })
  it('re-emits done when the dropzone finishes a batch', async () => {
    const w = mountDialog()
    w.findComponent(MediaUploadDropzone).vm.$emit('done')
    await w.vm.$nextTick()
    expect(w.emitted('done')).toBeTruthy()
  })

  // reka points DialogContent's aria-describedby at a DialogDescription id whether or not one is
  // rendered, and warns on mount when nothing in the document carries that id -- so the warning is
  // not cosmetic: without a description, assistive tech follows a dangling reference.
  //
  // Mounted attached, unlike every other test in this file, and that is load-bearing: reka resolves
  // the id with document.getElementById, which cannot see a detached wrapper. Mounted the usual way
  // this assertion fails whether or not the description exists, so it would prove nothing.
  it('renders a description, so reka does not warn about a dangling aria-describedby', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const container = document.body.appendChild(document.createElement('div'))
    const w = mount(MediaUploadDialog, {
      props: { visible: true },
      global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
      attachTo: container,
    })
    await flushPromises()
    const messages = warn.mock.calls.map((c) => c.map(String).join(' '))
    expect(messages.filter((m) => m.includes('Missing `Description`'))).toEqual([])
    w.unmount()
    container.remove()
  })
})
