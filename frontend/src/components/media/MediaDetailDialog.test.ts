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

const confirmRequire = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
const toastAdd = vi.fn()
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    detailTitle: 'File details', fieldTitle: 'Title', fieldAlt: 'Alt text', fileUrl: 'File URL',
    copyUrl: 'Copy URL', urlCopied: 'URL copied', copyFailed: 'Could not copy URL', status: 'Status',
    save: 'Save', delete: 'Delete file', saveConflict: 'Changed elsewhere', saveFailed: 'Save failed',
    colSize: 'Size', colDimensions: 'Dimensions', colUploaded: 'Uploaded',
    folderField: 'Folder', folderUncategorized: 'Uncategorized',
  }, confirm: {
    softDeleteHeader: 'Move to trash', softDeleteMessage: 'Move this item to trash? You can restore it later.',
    hardDeleteHeader: 'Confirm delete', hardDeleteMessage: 'Delete this item? This cannot be undone.',
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

// Two-level folder tree: root 'a', child 'b' (parentId 'a').
const folderRows = [
  { id: 'a', name: 'Root A', parentId: null },
  { id: 'b', name: 'Child B', parentId: 'a' },
]

function mountDialog() {
  return mount(MediaDetailDialog, {
    props: { file: { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024 }, canWrite: true, canDelete: true },
    global: { plugins: [i18n], stubs: {
      Dialog: DialogStub,
      InputText: { name: 'InputText', template: '<input />' },
      Button: { name: 'Button', template: '<button><slot /></button>', props: ['label'] },
      SelectButton: { name: 'SelectButton', template: '<div />' },
      ConfirmDialog: { name: 'ConfirmDialog', template: '<div />' },
      TreeSelect: { name: 'TreeSelect', template: '<div />', props: ['modelValue', 'options', 'disabled'] },
    } },
  })
}

describe('MediaDetailDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    confirmRequire.mockClear(); toastAdd.mockClear()
    seedStores()
    // Default: no mediafolder rows unless a test overrides. Component load() is only expected
    // to call itemsApi.list('mediafolder', ...) — never itemsApi.list for anything else.
    vi.spyOn(itemsApi, 'list').mockImplementation((collection) => {
      if (collection === 'mediafolder') return Promise.resolve({ data: folderRows, total: folderRows.length })
      return Promise.resolve({ data: [], total: 0 })
    })
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
      configurable: true,
    })
  })

  it('loads the item and exposes a per-locale model', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    // Relation FKs like folderId are only projected under `deep` expansion (nested as
    // `folder: { id }`) -- the load must request it, or folderId always reads back undefined.
    expect(itemsApi.get).toHaveBeenCalledWith('file', 'f1', { deep: ['folder'] })
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

  it('copies the file URL and shows a success toast', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    await (w.vm as unknown as { onCopyUrl: () => Promise<void> }).onCopyUrl()
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(expect.stringContaining('f1'))
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success', summary: 'URL copied' }))
  })

  it('shows a failure toast when the clipboard write rejects, instead of failing silently', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'))
    const w = mountDialog()
    await flushPromises()
    await (w.vm as unknown as { onCopyUrl: () => Promise<void> }).onCopyUrl()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: 'Could not copy URL' }))
  })

  it('disables the Title/Alt inputs while loading, not just when read-only', async () => {
    let resolveGet!: (v: unknown) => void
    vi.spyOn(itemsApi, 'get').mockReturnValue(new Promise((r) => (resolveGet = r)) as never)
    const w = mountDialog()
    // Synchronous portion of load() (up to its first await) has already run as part of mount, so
    // the initial render reflects loading === true before we resolve the pending itemsApi.get.
    const inputs = w.findAllComponents({ name: 'InputText' })
    // The two per-locale text inputs (title, alt) are the first two InputText instances rendered.
    // Vue renders a `true` boolean attribute as the empty string (`disabled=""`), so assert
    // presence rather than truthiness.
    expect(inputs[0].attributes('disabled')).toBe('')
    resolveGet(item)
    await flushPromises()
    expect(w.findAllComponents({ name: 'InputText' })[0].attributes('disabled')).toBeFalsy()
  })

  it('confirms with soft-delete copy, deletes via filesApi.remove (trash, no purge) after accept, and emits deleted', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const remove = vi.spyOn(filesApi, 'remove').mockResolvedValue()
    const w = mountDialog()
    await flushPromises()
    ;(w.vm as unknown as { onDelete: () => void }).onDelete()
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    // The dialog's delete action trashes (filesApi.remove defaults to soft-delete), so its confirm
    // copy must match -- not the hard-delete "cannot be undone" copy.
    expect(confirmRequire).toHaveBeenCalledWith(expect.objectContaining({
      header: 'Move to trash',
      message: 'Move this item to trash? You can restore it later.',
    }))
    const accept = confirmRequire.mock.calls[0][0].accept as () => Promise<void>
    await accept()
    await flushPromises()
    expect(remove).toHaveBeenCalledWith('f1')
    expect(w.emitted('deleted')).toBeTruthy()
  })

  it('does not render an "open in full editor" link: no such button, no vue-router import', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    expect(w.text()).not.toContain('Open in full editor')
    expect(w.findAll('.pi-external-link').length).toBe(0)
  })

  it('loads mediafolder rows and shows a Folder TreeSelect', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    expect(itemsApi.list).toHaveBeenCalledWith('mediafolder', expect.objectContaining({ page: 0, rows: 500, sort: 'name', deep: ['parent'] }))
    expect(w.findComponent({ name: 'TreeSelect' }).exists()).toBe(true)
  })

  // The items API never returns a flat `folderId` column -- [CmsRelation] FKs only appear once
  // `deep=folder` expands the relation, nested as `folder: { id, ... }` under the nav-property
  // name. These cases mock that real response shape; a regression back to reading item.folderId
  // directly would leave folderId permanently null/undefined and must fail these.
  it('selects the current folder as the initial TreeSelect value from the deep-expanded item.folder', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ ...item, folder: { id: 'b', name: 'Child B' } } as never)
    const w = mountDialog()
    await flushPromises()
    const vm = w.vm as unknown as { folderId: string | null }
    expect(vm.folderId).toBe('b')
  })

  it('selects the Uncategorized node as the initial TreeSelect value when item.folder is absent (unfiled)', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ ...item, folder: null } as never)
    const w = mountDialog()
    await flushPromises()
    const vm = w.vm as unknown as { folderId: string | null }
    expect(vm.folderId).toBeNull()
  })

  it('sends the selected folder id in the update payload on save', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ ...item, folder: null } as never)
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({} as never)
    const w = mountDialog()
    await flushPromises()
    const vm = w.vm as unknown as { onFolderChange: (s: Record<string, boolean>) => void; onSave: () => Promise<void> }
    vm.onFolderChange({ b: true })
    await vm.onSave()
    await flushPromises()
    expect(update).toHaveBeenCalledWith('file', 'f1', expect.objectContaining({ folderId: 'b' }))
  })

  it('sends folderId null when Uncategorized is selected on save', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ ...item, folder: { id: 'b', name: 'Child B' } } as never)
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({} as never)
    const w = mountDialog()
    await flushPromises()
    const vm = w.vm as unknown as { onFolderChange: (s: Record<string, boolean>) => void; onSave: () => Promise<void> }
    vm.onFolderChange({ __unfiled: true })
    await vm.onSave()
    await flushPromises()
    expect(update).toHaveBeenCalledWith('file', 'f1', expect.objectContaining({ folderId: null }))
  })

  it('preserves a filed file\'s folder on save (data-loss regression guard): the folder must not be nulled out', async () => {
    // This is the exact CRITICAL data-loss path: a file that IS filed under folder 'b' is loaded,
    // the user changes nothing, and hits Save. Before the fix, item.folderId was always undefined
    // (the API never returns it outside `deep`), so folderId.value silently became null and Save
    // unfiled the file. With deep:['folder'], the nested item.folder.id must populate folderId,
    // and an untouched save must send that same folder id back, not null.
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ ...item, folder: { id: 'b', name: 'Child B' } } as never)
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({} as never)
    const w = mountDialog()
    await flushPromises()
    const vm = w.vm as unknown as { folderId: string | null; onSave: () => Promise<void> }
    expect(vm.folderId).toBe('b')
    await vm.onSave()
    await flushPromises()
    expect(update).toHaveBeenCalledWith('file', 'f1', expect.objectContaining({ folderId: 'b' }))
  })

  it('degrades gracefully (no blocking) when mediafolder loading fails', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    vi.spyOn(itemsApi, 'list').mockRejectedValue(new Error('boom'))
    const w = mountDialog()
    await flushPromises()
    expect((w.vm as unknown as { error: string }).error).toBe('')
    expect(w.findComponent({ name: 'TreeSelect' }).exists()).toBe(true)
  })
})
