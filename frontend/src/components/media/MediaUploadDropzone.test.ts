import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import MediaUploadDropzone from './MediaUploadDropzone.vue'
import { filesApi } from '../../api/filesApi'

const meta = (id: string) => ({ id, fileName: id, contentType: 'image/png', size: 1, width: 1, height: 1, status: 'published' })

describe('MediaUploadDropzone', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('uploads each file and emits uploaded per success', async () => {
    vi.spyOn(filesApi, 'upload').mockImplementation((f) => Promise.resolve(meta((f as File).name)))
    const w = mount(MediaUploadDropzone)
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
    const w = mount(MediaUploadDropzone)
    await (w.vm as unknown as { uploadFiles: (f: File[]) => Promise<void> }).uploadFiles([
      new File(['a'], 'ok', { type: 'image/png' }),
      new File(['b'], 'bad', { type: 'image/png' }),
    ])
    expect(w.emitted('uploaded')).toHaveLength(1)
    expect(w.text()).toContain('too big')
  })
})
