import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import MediaLibraryView from './MediaLibraryView.vue'
import MediaUploadDropzone from '../components/media/MediaUploadDropzone.vue'
import { itemsApi } from '../api/itemsApi'
import { filesApi } from '../api/filesApi'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

const rows = [{ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }]

describe('MediaLibraryView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    push.mockClear()
  })

  it('loads files into the grid on mount', async () => {
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mount(MediaLibraryView, { global: { stubs: { RouterLink: true } } })
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('file', expect.objectContaining({ page: 0 }))
    expect(w.findAll('.media-tile')).toHaveLength(1)
  })

  it('removes a file then refreshes', async () => {
    const list = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const del = vi.spyOn(filesApi, 'remove').mockResolvedValue()
    const w = mount(MediaLibraryView, { global: { stubs: { RouterLink: true } } })
    await flushPromises()
    await (w.vm as unknown as { onDelete: (id: string) => Promise<void> }).onDelete('f1')
    expect(del).toHaveBeenCalledWith('f1')
    expect(list).toHaveBeenCalledTimes(2)
  })

  it('reloads once per upload batch (on the dropzone "done" event), not once per uploaded file', async () => {
    const list = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mount(MediaLibraryView, { global: { stubs: { RouterLink: true } } })
    await flushPromises()
    expect(list).toHaveBeenCalledTimes(1)

    const dropzone = w.findComponent(MediaUploadDropzone)
    // Simulate an N-file batch: several 'uploaded' events, then a single 'done'.
    dropzone.vm.$emit('uploaded', { id: 'f2' })
    dropzone.vm.$emit('uploaded', { id: 'f3' })
    dropzone.vm.$emit('done')
    await flushPromises()

    expect(list).toHaveBeenCalledTimes(2)
  })
})
