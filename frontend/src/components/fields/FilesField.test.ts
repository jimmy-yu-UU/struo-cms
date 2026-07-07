import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import FilesField from './FilesField.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'gallery', label: 'Gallery', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}

const f1 = { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }
const f2 = { id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2 }
const f3 = { id: 'f3', fileName: 'c.png', contentType: 'image/png', size: 3 }

const stubs = { OrderList: true, Dialog: true, Button: true, InputText: true, MediaGrid: true, FileThumbnail: true }

function setupStores() {
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
}

describe('FilesField', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('resolves the model ids in order on mount', async () => {
    setupStores()
    // Return out of model order to prove the component re-orders to the model.
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f2, f1], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { stubs } })
    await flushPromises()
    expect((w.vm as unknown as { currentIds: () => string[] }).currentIds()).toEqual(['f1', 'f2'])
  })

  it('keeps a missing id as a raw-id fallback row', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1], total: 1 }) // f2 gone
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { stubs } })
    await flushPromises()
    expect((w.vm as unknown as { currentIds: () => string[] }).currentIds()).toEqual(['f1', 'f2'])
  })

  it('reorder emits the new id order', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { stubs } })
    await flushPromises()
    ;(w.vm as unknown as { onReorder: (v: unknown[]) => void }).onReorder([f2, f1])
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f2', 'f1']])
  })

  it('removeAt emits the shortened array immutably', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { stubs } })
    await flushPromises()
    ;(w.vm as unknown as { removeAt: (i: number) => void }).removeAt(0)
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f2']])
  })

  it('toggling an unselected file in the picker appends it last', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list')
    listSpy.mockResolvedValueOnce({ data: [f1], total: 1 })   // mount resolve
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1'] }, global: { stubs } })
    await flushPromises()
    listSpy.mockResolvedValueOnce({ data: [f1, f3], total: 2 }) // dialog options
    await (w.vm as unknown as { openDialog: () => Promise<void> }).openDialog()
    await flushPromises()
    ;(w.vm as unknown as { toggle: (id: string) => void }).toggle('f3')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f1', 'f3']])
  })

  it('toggling an already-selected file removes it', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { stubs } })
    await flushPromises()
    ;(w.vm as unknown as { toggle: (id: string) => void }).toggle('f1')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f2']])
  })
})
