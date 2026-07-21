import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaUploadDialog from './MediaUploadDialog.vue'
import MediaUploadDropzone from './MediaUploadDropzone.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: { uploadTitle: 'Upload files', dropzone: 'Drop files here or click to upload' } } },
})

// Stub PrimeVue Dialog to a passthrough so the slot renders without teleport.
const DialogStub = { name: 'Dialog', template: '<div><slot /></div>' }

function mountDialog() {
  return mount(MediaUploadDialog, {
    props: { visible: true },
    global: { plugins: [i18n], stubs: { Dialog: DialogStub } },
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
