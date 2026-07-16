import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import Button from 'primevue/button'
import MediaLibraryView from './MediaLibraryView.vue'
import MediaUploadDropzone from '../components/media/MediaUploadDropzone.vue'
import { itemsApi } from '../api/itemsApi'
import { filesApi } from '../api/filesApi'
import { useAuthStore } from '../stores/authStore'
import type { CurrentUser } from '../stores/authStore'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

const confirmRequire = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
vi.mock('primevue/confirmdialog', () => ({ default: { name: 'ConfirmDialog', template: '<div />' } }))

const rows = [{ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }]

function seedUser(perms: Partial<Record<'read' | 'write' | 'delete', boolean>>): void {
  const auth = useAuthStore()
  auth.user = {
    id: 'u1',
    isSuperAdmin: false,
    permissions: { file: { read: true, write: false, delete: false, ...perms } },
  } as CurrentUser
}

function actionLabels(w: ReturnType<typeof mount>): string[] {
  return w.findAllComponents(Button).map((b) => b.props('label') as string)
}

describe('MediaLibraryView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    push.mockClear()
    confirmRequire.mockClear()
  })

  it('loads files into the grid on mount', async () => {
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mount(MediaLibraryView, { global: { stubs: { RouterLink: true } } })
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('file', expect.objectContaining({ page: 0 }))
    expect(w.findAll('.media-tile')).toHaveLength(1)
  })

  it('confirms before deleting: remove is NOT called until accept runs', async () => {
    const list = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const del = vi.spyOn(filesApi, 'remove').mockResolvedValue()
    const w = mount(MediaLibraryView, { global: { stubs: { RouterLink: true } } })
    await flushPromises()

    await (w.vm as unknown as { onDelete: (id: string) => void }).onDelete('f1')
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    expect(del).not.toHaveBeenCalled()
    expect(list).toHaveBeenCalledTimes(1) // no reload yet

    const accept = confirmRequire.mock.calls[0][0].accept as () => Promise<void>
    await accept()
    await flushPromises()
    expect(del).toHaveBeenCalledWith('f1')
    expect(list).toHaveBeenCalledTimes(2)
  })

  it('hides Delete/Edit actions for a user without file permissions', async () => {
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mount(MediaLibraryView, { global: { stubs: { RouterLink: true } } })
    seedUser({ write: false, delete: false })
    await flushPromises()
    const labels = actionLabels(w)
    expect(labels).not.toContain('Delete')
    expect(labels).not.toContain('Edit')
  })

  it('renders Delete/Edit actions for a user with write+delete on file', async () => {
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows as never, total: 1 })
    const w = mount(MediaLibraryView, { global: { stubs: { RouterLink: true } } })
    seedUser({ write: true, delete: true })
    await flushPromises()
    const labels = actionLabels(w)
    expect(labels).toContain('Delete')
    expect(labels).toContain('Edit')
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
