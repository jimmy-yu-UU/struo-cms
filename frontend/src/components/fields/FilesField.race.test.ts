import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import FilesField from './FilesField.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import type { FieldMeta } from '../../types/schema'

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en: {} } })
const f1 = { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }
const f2 = { id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2 }
const field = { name: 'gallery', label: 'Gallery', interface: 'files', required: false, searchable: false, sortable: false,
  readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false } as FieldMeta

type ListResult = { data: unknown[]; total: number }

describe('FilesField resolve ordering', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    useLanguageStore().languages = [{ code: 'en', name: 'English', isDefault: true }]
  })

  it('keeps the newest model when an earlier resolve finishes last', async () => {
    const resolvers: Array<(r: ListResult) => void> = []
    vi.spyOn(itemsApi, 'list').mockImplementation(() => new Promise<ListResult>((r) => { resolvers.push(r) }) as never)
    const w = mount(FilesField, {
      props: { field, modelValue: ['f1'] },
      global: { plugins: [i18n], stubs: { Dialog: true, MediaGrid: true } },
    })
    await w.setProps({ modelValue: ['f2'] })
    resolvers[1]({ data: [f2], total: 1 })
    await flushPromises()
    resolvers[0]({ data: [f1], total: 1 })
    await flushPromises()
    expect(w.text()).toContain('b.png')
    expect(w.text()).not.toContain('a.png')
  })
})
