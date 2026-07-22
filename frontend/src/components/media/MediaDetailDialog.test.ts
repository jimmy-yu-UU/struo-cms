import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import MediaDetailDialog from './MediaDetailDialog.vue'
import { itemsApi } from '../../api/itemsApi'
import { filesApi } from '../../api/filesApi'
import { ApiError } from '../../api/apiClient'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
const confirmRequire = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
const toastAdd = vi.fn()
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    detailTitle: 'File details', fieldTitle: 'Title', fieldAlt: 'Alt text', fileUrl: 'File URL',
    copyUrl: 'Copy URL', urlCopied: 'URL copied', status: 'Status', openInEditor: 'Open in full editor',
    save: 'Save', delete: 'Delete file', saveConflict: 'Changed elsewhere', saveFailed: 'Save failed',
    colSize: 'Size', colDimensions: 'Dimensions', colUploaded: 'Uploaded',
  } } },
})

const DialogStub = { name: 'Dialog', template: '<div v-if="visible"><slot /><slot name="footer" /></div>', props: ['visible'] }

const fileMeta = {
  name: 'file', label: 'File',
  fields: [
    { name: 'fileName', label: 'File Name', interface: 'text', required: false, searchable: true, sortable: false, readOnly: true, hidden: false, translatable: false, sort: 1, isSystem: false },
    { name: 'contentType', label: 'Content Type', interface: 'text', required: false, searchable: false, sortable: false, readOnly: true, hidden: false, translatable: false, sort: 2, isSystem: false },
    { name: 'size', label: 'Size', interface: 'number', required: false, searchable: false, sortable: false, readOnly: true, hidden: false, translatable: false, sort: 3, isSystem: false },
    { name: 'status', label: 'Status', interface: 'select', required: false, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: false, sort: 6, isSystem: false },
    { name: 'title', label: 'Title', interface: 'text', required: false, searchable: true, sortable: false, readOnly: false, hidden: false, translatable: true, sort: 1, isSystem: false },
    { name: 'alt', label: 'Alt', interface: 'text', required: false, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: true, sort: 2, isSystem: false },
  ],
  relations: [],
}

function seedStores() {
  const schema = useSchemaStore()
  schema.collections = [fileMeta as never]
  schema.loaded = true
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }, { code: 'zh-TW', name: '繁中', isDefault: false }]
  lang.loaded = true
}

const item = {
  id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024, width: 800, height: 600, status: 'published',
  createdAt: '2026-07-01T00:00:00Z', version: 3,
  translations: { en: { title: 'Hello', alt: 'An image' }, 'zh-TW': { title: '', alt: '' } },
}

function mountDialog() {
  return mount(MediaDetailDialog, {
    props: { file: { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024 }, canWrite: true, canDelete: true },
    global: { plugins: [i18n], stubs: { Dialog: DialogStub, InputText: { name: 'InputText', template: '<input />' }, Button: { name: 'Button', template: '<button><slot /></button>', props: ['label'] }, SelectButton: { name: 'SelectButton', template: '<div />' }, ConfirmDialog: { name: 'ConfirmDialog', template: '<div />' } } },
  })
}

describe('MediaDetailDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    push.mockClear(); confirmRequire.mockClear(); toastAdd.mockClear()
    seedStores()
  })

  it('loads the item and exposes a per-locale model', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    expect(itemsApi.get).toHaveBeenCalledWith('file', 'f1')
    const vm = w.vm as unknown as { model: { translations: Record<string, Record<string, unknown>>; version?: number } }
    expect(vm.model.translations.en.title).toBe('Hello')
    expect(vm.model.version).toBe(3)
  })

  it('saves via itemsApi.update with a payload including version, then emits saved', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({} as never)
    const w = mountDialog()
    await flushPromises()
    await (w.vm as unknown as { onSave: () => Promise<void> }).onSave()
    await flushPromises()
    expect(update).toHaveBeenCalledWith('file', 'f1', expect.objectContaining({ version: 3 }))
    expect(w.emitted('saved')).toBeTruthy()
  })

  it('shows a conflict message on 409 VERSION_CONFLICT and does not emit saved', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    vi.spyOn(itemsApi, 'update').mockRejectedValue(new ApiError(409, 'conflict', 'VERSION_CONFLICT'))
    const w = mountDialog()
    await flushPromises()
    await (w.vm as unknown as { onSave: () => Promise<void> }).onSave()
    await flushPromises()
    expect((w.vm as unknown as { conflict: boolean }).conflict).toBe(true)
    expect(w.emitted('saved')).toBeFalsy()
  })

  it('preserves per-locale edits when switching locales, then saves both in the payload', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({} as never)
    const w = mountDialog()
    await flushPromises()
    const vm = w.vm as unknown as {
      setField: (n: string, v: string) => void
      activeLocale: string
      model: { translations: Record<string, Record<string, unknown>> }
      onSave: () => Promise<void>
    }

    vm.activeLocale = 'en'
    await w.vm.$nextTick()
    vm.setField('title', 'Hello EN')
    await w.vm.$nextTick()

    vm.activeLocale = 'zh-TW'
    await w.vm.$nextTick()
    vm.setField('title', '哈囉')
    await w.vm.$nextTick()

    expect(vm.model.translations.en.title).toBe('Hello EN')
    expect(vm.model.translations['zh-TW'].title).toBe('哈囉')

    await vm.onSave()
    await flushPromises()

    expect(update).toHaveBeenCalledWith('file', 'f1', expect.objectContaining({
      translations: expect.objectContaining({
        en: expect.objectContaining({ title: 'Hello EN' }),
        'zh-TW': expect.objectContaining({ title: '哈囉' }),
      }),
    }))
  })

  it('recovers from 409 by refreshing the version while preserving edits, so a second save succeeds', async () => {
    const get = vi.spyOn(itemsApi, 'get')
      .mockResolvedValueOnce(item as never) // initial load
      .mockResolvedValueOnce({ ...item, version: 5 } as never) // recovery refetch
    const update = vi.spyOn(itemsApi, 'update')
      .mockRejectedValueOnce(new ApiError(409, 'conflict', 'VERSION_CONFLICT'))
      .mockResolvedValueOnce({} as never)
    const w = mountDialog()
    await flushPromises()
    const vm = w.vm as unknown as {
      setField: (n: string, v: string) => void
      activeLocale: string
      model: { version?: number; translations: Record<string, Record<string, unknown>> }
      onSave: () => Promise<void>
      conflict: boolean
    }

    vm.activeLocale = 'en'
    await w.vm.$nextTick()
    vm.setField('title', 'Edited Title')
    await w.vm.$nextTick()

    await vm.onSave()
    await flushPromises()

    // conflict recovery: version refreshed, edits preserved, no data loss
    expect(vm.conflict).toBe(true)
    expect(vm.model.version).toBe(5)
    expect(vm.model.translations.en.title).toBe('Edited Title')
    expect(get).toHaveBeenCalledTimes(2)

    // a subsequent save now sends the fresh version and succeeds
    await vm.onSave()
    await flushPromises()

    expect(update).toHaveBeenLastCalledWith('file', 'f1', expect.objectContaining({
      version: 5,
      translations: expect.objectContaining({ en: expect.objectContaining({ title: 'Edited Title' }) }),
    }))
    expect(w.emitted('saved')).toBeTruthy()
  })

  it('deletes via filesApi.remove after confirm accept and emits deleted', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const remove = vi.spyOn(filesApi, 'remove').mockResolvedValue()
    const w = mountDialog()
    await flushPromises()
    ;(w.vm as unknown as { onDelete: () => void }).onDelete()
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    const accept = confirmRequire.mock.calls[0][0].accept as () => Promise<void>
    await accept()
    await flushPromises()
    expect(remove).toHaveBeenCalledWith('f1')
    expect(w.emitted('deleted')).toBeTruthy()
  })
})
