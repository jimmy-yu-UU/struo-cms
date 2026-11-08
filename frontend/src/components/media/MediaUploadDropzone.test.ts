import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaUploadDropzone from './MediaUploadDropzone.vue'
import { filesApi } from '../../api/filesApi'
import { ApiError } from '../../api/apiClient'
import { catalogs } from '../../locales'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en: { media: { dropzone: 'Drop files here or click to upload' } } },
})

const meta = (id: string) => ({ id, fileName: id, contentType: 'image/png', size: 1, width: 1, height: 1, status: 'published', folderId: null })

describe('MediaUploadDropzone', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('uploads each file and emits uploaded per success', async () => {
    vi.spyOn(filesApi, 'upload').mockImplementation((f) => Promise.resolve(meta((f as File).name)))
    const w = mount(MediaUploadDropzone, { global: { plugins: [i18n] } })
    await (w.vm as unknown as { uploadFiles: (f: File[]) => Promise<void> }).uploadFiles([
      new File(['a'], 'a', { type: 'image/png' }),
      new File(['b'], 'b', { type: 'image/png' }),
    ])
    expect(w.emitted('uploaded')).toHaveLength(2)
    expect(w.emitted('done')).toHaveLength(1)
  })

  it('isolates a failing upload and still emits done', async () => {
    vi.spyOn(filesApi, 'upload').mockImplementation((f) =>
      (f as File).name === 'bad' ? Promise.reject(new Error('too big')) : Promise.resolve(meta((f as File).name)))
    const w = mount(MediaUploadDropzone, { global: { plugins: [i18n] } })
    await (w.vm as unknown as { uploadFiles: (f: File[]) => Promise<void> }).uploadFiles([
      new File(['a'], 'ok', { type: 'image/png' }),
      new File(['b'], 'bad', { type: 'image/png' }),
    ])
    expect(w.emitted('uploaded')).toHaveLength(1)
    expect(w.text()).toContain('too big')
  })

  it('passes the folderId prop through to filesApi.upload', async () => {
    const upload = vi.spyOn(filesApi, 'upload').mockImplementation((f) => Promise.resolve(meta((f as File).name)))
    const w = mount(MediaUploadDropzone, { props: { folderId: 'fid' }, global: { plugins: [i18n] } })
    const file = new File(['a'], 'a', { type: 'image/png' })
    await (w.vm as unknown as { uploadFiles: (f: File[]) => Promise<void> }).uploadFiles([file])
    expect(upload).toHaveBeenCalledWith(file, 'fid')
  })

  describe('an upload over the size limit', () => {
    const zhI18n = () => createI18n({ legacy: false, locale: 'zh-TW', fallbackLocale: 'en', messages: catalogs as never })

    async function uploadRejectedWith(err: Error, plugin = zhI18n()) {
      vi.spyOn(filesApi, 'upload').mockRejectedValue(err)
      const w = mount(MediaUploadDropzone, { global: { plugins: [plugin] } })
      await (w.vm as unknown as { uploadFiles: (f: File[]) => Promise<void> }).uploadFiles([
        new File(['a'], 'big.bin', { type: 'application/octet-stream' }),
      ])
      return w
    }

    it('names the limit from the server details, in zh-TW, never the English prose', async () => {
      const w = await uploadRejectedWith(new ApiError(413, 'File exceeds the maximum size of 26214400 bytes.',
        'PAYLOAD_TOO_LARGE', [{ field: 'file', message: 'x', maxBytes: 26214400 }]))
      expect(w.text()).toContain('big.bin — 檔案超過上限（25.0 MB）。')
      expect(w.text()).not.toContain('maximum size')
    })

    it('answers a proxy 413 without a JSON body with the same sentence minus the size', async () => {
      const w = await uploadRejectedWith(new ApiError(413, 'Request failed (413)'))
      expect(w.text()).toContain('big.bin — 檔案超過上限。')
      expect(w.text()).not.toContain('Request failed')
    })

    it('answers a 413 envelope without details the same way', async () => {
      const w = await uploadRejectedWith(new ApiError(413, 'Request body is too large.', 'PAYLOAD_TOO_LARGE'))
      expect(w.text()).toContain('檔案超過上限。')
      expect(w.text()).not.toContain('too large')
    })

    it('writes the en sentence for the en UI', async () => {
      const enI18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: catalogs as never })
      const w = await uploadRejectedWith(new ApiError(413, 'x', 'PAYLOAD_TOO_LARGE',
        [{ field: 'file', message: 'x', maxBytes: 26214400 }]), enI18n)
      expect(w.text()).toContain('The file is over the size limit (25.0 MB).')
    })
  })
})
