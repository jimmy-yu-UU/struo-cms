import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import FilePicker from './FilePicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en: { fields: { noFileSelected: 'No file selected' } } } })
const f1 = { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }
const f2 = { id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2 }

type Row = typeof f1
type Pending = { resolve: (r: Row) => void; reject: (e: unknown) => void }

describe('FilePicker resolveCurrent ordering', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    useLanguageStore().languages = [{ code: 'en', name: 'English', isDefault: true }]
  })

  function setup() {
    const pending = new Map<string, Pending>()
    vi.spyOn(itemsApi, 'get').mockImplementation(((_c: string, id: string) =>
      new Promise<Row>((resolve, reject) => { pending.set(id, { resolve, reject }) })) as never)
    const w = mount(FilePicker, {
      props: { modelValue: 'f1' },
      global: { plugins: [i18n], stubs: { Dialog: true, Button: true, Input: true, MediaGrid: true } },
    })
    return { w, pending }
  }

  it('shows the newest value when an earlier lookup resolves last', async () => {
    const { w, pending } = setup()
    await w.setProps({ modelValue: 'f2' })
    pending.get('f2')!.resolve(f2)
    await flushPromises()
    pending.get('f1')!.resolve(f1)
    await flushPromises()
    expect(w.text()).toContain('b.png')
    expect(w.text()).not.toContain('a.png')
  })

  it('does not mark the newest value missing when an earlier lookup fails last', async () => {
    const { w, pending } = setup()
    await w.setProps({ modelValue: 'f2' })
    pending.get('f2')!.resolve(f2)
    await flushPromises()
    pending.get('f1')!.reject(new Error('gone'))
    await flushPromises()
    expect(w.text()).toContain('b.png')
    expect(w.text()).not.toContain('f1')
  })
})
