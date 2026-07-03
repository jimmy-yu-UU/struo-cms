import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import FilePicker from './FilePicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'

const rows = [{ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }]

const stubs = {
  Dialog: true,
  Button: true,
  InputText: true,
  MediaGrid: true,
}

function setupStores() {
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  return { lang }
}

describe('FilePicker', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('resolves and shows the current value on mount', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 })
    const w = mount(FilePicker, { props: { modelValue: 'f1', image: true }, global: { stubs } })
    await flushPromises()
    expect(w.text()).toContain('a.png')
  })

  it('clear emits null', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 })
    const w = mount(FilePicker, { props: { modelValue: 'f1' }, global: { stubs } })
    await flushPromises()
    ;(w.vm as unknown as { clear: () => void }).clear()
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([null])
  })

  it('selecting a file emits its id and closes the dialog', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows, total: 1 })
    const w = mount(FilePicker, { props: { modelValue: null }, global: { stubs } })
    await (w.vm as unknown as { openDialog: () => Promise<void> }).openDialog()
    await flushPromises()
    ;(w.vm as unknown as { onSelect: (id: string) => void }).onSelect('f1')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['f1'])
  })

  it('falls back to showing the id when the current file is gone', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'get').mockRejectedValue(new Error('404'))
    const w = mount(FilePicker, { props: { modelValue: 'ghost' }, global: { stubs } })
    await flushPromises()
    expect(w.text()).toContain('ghost')
  })
})
