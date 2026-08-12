import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaUploadDialog from './MediaUploadDialog.vue'
import MediaUploadDropzone from './MediaUploadDropzone.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: { uploadTitle: 'Upload files', dropzone: 'Drop files here or click to upload' } } },
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
})
