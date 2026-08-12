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

const confirmRequire = vi.fn<(req: unknown) => Promise<boolean>>(() => Promise.resolve(true))
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    detailTitle: 'File details', fieldTitle: 'Title', fieldAlt: 'Alt text', fileUrl: 'File URL',
    copyUrl: 'Copy URL', urlCopied: 'URL copied', copyFailed: 'Could not copy URL', status: 'Status',
    save: 'Save', saving: 'Saving…', delete: 'Delete file', saveConflict: 'Changed elsewhere', saveFailed: 'Save failed',
    colSize: 'Size', colDimensions: 'Dimensions', colUploaded: 'Uploaded',
    folderField: 'Folder', folderUncategorized: 'Uncategorized',
  }, confirm: {
    softDeleteHeader: 'Move to trash', softDeleteMessage: 'Move this item to trash? You can restore it later.',
    hardDeleteHeader: 'Confirm delete', hardDeleteMessage: 'Delete this item? This cannot be undone.',
  }, fields: {
    selectAnItem: 'Select an item',
  } } },
})

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

type MountFile = { id: string; fileName: string; contentType: string; size: number } | null

function mountDialog(overrides: { file?: MountFile; canWrite?: boolean; canDelete?: boolean } = {}) {
  return mount(MediaDetailDialog, {
    props: {
      file: { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024 },
      canWrite: true,
      canDelete: true,
      ...overrides,
    },
    global: {
      plugins: [i18n],
      // reka's portal wrapper is itself named Teleport and collides with VTU's stub, dropping the
      // whole dialog body. Nothing here asserts against document.body, so the in-tree render is fine.
      stubs: { teleport: true },
      renderStubDefaultSlot: true,
    },
  })
}

describe('MediaDetailDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    confirmRequire.mockReset(); confirmRequire.mockResolvedValue(true)
    toastAdd.mockClear()
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
    const inputs = w.findAll('input[data-slot="input"]')
    // The two per-locale text inputs (title, alt) are the first two vendored Input instances
    // rendered. Vue renders a `true` boolean attribute as the empty string (`disabled=""`), so
    // assert presence rather than truthiness.
    expect(inputs[0].attributes('disabled')).toBe('')
    resolveGet(item)
    await flushPromises()
    expect(w.findAll('input[data-slot="input"]')[0].attributes('disabled')).toBeFalsy()
  })

  it('renders the vendored dialog and inputs, not PrimeVue ones', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    // MediaLibraryView mounts this dialog unconditionally with :file="selected", and `selected`
    // starts null -- taking that same null-then-real transition here (instead of a prop that is
    // already non-null at setup) matches the real initial-mount path.
    const w = mountDialog({ file: null })
    await flushPromises()
    await w.setProps({ file: { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024 } })
    await flushPromises()
    expect(w.find('[data-slot="dialog-title"]').exists()).toBe(true)
    // Title, Alt and the read-only File URL.
    const inputs = () => w.findAll('[data-slot="input"]')
    expect(inputs()).toHaveLength(3)
    expect(w.findComponent({ name: 'SelectButton' }).exists()).toBe(false)
    const fileUrlInput = () => inputs()[2].element as HTMLInputElement
    expect(fileUrlInput().value).toContain('f1')
    expect(inputs()[2].attributes('readonly')).toBe('')

    // The dialog stays open across this second file (both props.file values are non-null, so
    // reka's Presence never unmounts/remounts DialogScrollContent) -- a `defaultValue`-only
    // binding would leave the URL box showing 'f1' forever once the user switches files without
    // closing the dialog in between.
    await w.setProps({ file: { id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2048 } })
    await flushPromises()
    expect(fileUrlInput().value).toContain('f2')
  })

  it('switches locale through a ToggleGroup and ignores its deselect emit', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    expect(w.find('[data-slot="toggle-group"]').exists()).toBe(true)
    const items = () => w.findAll('[data-slot="toggle-group-item"]')
    // 'en' is the default locale, so it starts pressed; 'zh-TW' does not.
    expect(items()[0].attributes('data-state')).toBe('on')
    expect(items()[1].attributes('data-state')).toBe('off')

    const vm = w.vm as unknown as { activeLocale: string; onLocaleToggle: (v: unknown) => void }
    vm.onLocaleToggle('zh-TW')
    await flushPromises()
    expect(vm.activeLocale).toBe('zh-TW')
    // The ToggleGroup's :model-value must actually be wired to activeLocale, not just the exposed
    // ref changing underneath a control that stopped tracking it.
    expect(items()[0].attributes('data-state')).toBe('off')
    expect(items()[1].attributes('data-state')).toBe('on')

    // reka's single-type ToggleGroup emits undefined when the pressed item is clicked again, and a
    // locale switcher has no "no locale" state to fall into.
    vm.onLocaleToggle(undefined)
    await flushPromises()
    expect(vm.activeLocale).toBe('zh-TW')
    expect(items()[1].attributes('data-state')).toBe('on')
  })

  it('reflects the stored Title and Alt, including a model replacement after mount', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    const titleInput = () => w.findAll('input[data-slot="input"]')[0].element as HTMLInputElement
    const altInput = () => w.findAll('input[data-slot="input"]')[1].element as HTMLInputElement
    expect(titleInput().value).toBe('Hello')
    expect(altInput().value).toBe('An image')
    // ItemFormView-style recovery replaces the whole model; an uncontrolled input would keep its
    // own stale state through that and the next save would write the stale value.
    const vm = w.vm as unknown as { setField: (n: 'title' | 'alt', v: string) => void }
    vm.setField('title', 'Replaced')
    vm.setField('alt', 'Replaced alt')
    await flushPromises()
    expect(titleInput().value).toBe('Replaced')
    expect(altInput().value).toBe('Replaced alt')
  })

  it('swaps the Save label while saving, since ui/button has no loading prop, and disables the button too', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    const saveButton = () => w.findAll('button').find((b) => b.text() === 'Save' || b.text() === 'Saving…')
    expect((w.vm as unknown as { saveLabel: string }).saveLabel).toBe('Save')
    expect(saveButton()?.attributes('disabled')).toBeFalsy()
    let release!: () => void
    vi.spyOn(itemsApi, 'update').mockReturnValue(new Promise((r) => { release = () => r(item as never) }) as never)
    const pending = (w.vm as unknown as { onSave: () => Promise<void> }).onSave()
    await flushPromises()
    expect((w.vm as unknown as { saveLabel: string }).saveLabel).toBe('Saving…')
    // ui/button has no `loading` prop, so double-submit protection on this versioned write comes
    // entirely from :disabled -- without it a second click re-enters onSave with the same version
    // and produces a spurious conflict.
    expect(saveButton()?.attributes('disabled')).toBe('')
    release()
    await pending
  })

  it('gives every dialog button an explicit type="button"', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    const buttons = w.findAll('button')
    expect(buttons.length).toBeGreaterThan(0)
    buttons.forEach((b) => expect(b.attributes('type')).toBe('button'))
  })

  it('renders distinct lucide icons for delete and copy-url, not swapped', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    const buttons = w.findAll('button')
    const deleteButton = buttons.find((b) => b.text().includes('Delete file'))
    const copyButton = buttons.find((b) => b.attributes('aria-label') === 'Copy URL')
    expect(deleteButton?.find('.lucide-trash-2').exists()).toBe(true)
    expect(deleteButton?.find('.lucide-copy').exists()).toBe(false)
    expect(copyButton?.find('.lucide-copy').exists()).toBe(true)
    expect(copyButton?.find('.lucide-trash-2').exists()).toBe(false)
  })

  it('deletes only when the confirmation resolves true', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const remove = vi.spyOn(filesApi, 'remove').mockResolvedValue(undefined as never)
    confirmRequire.mockResolvedValueOnce(false)
    const w = mountDialog()
    await flushPromises()
    await (w.vm as unknown as { onDelete: () => Promise<void> }).onDelete()
    expect(remove).not.toHaveBeenCalled()
    confirmRequire.mockResolvedValueOnce(true)
    await (w.vm as unknown as { onDelete: () => Promise<void> }).onDelete()
    await flushPromises()
    expect(remove).toHaveBeenCalledWith('f1')
    expect(w.emitted('deleted')).toBeTruthy()
  })

  it('requests the soft-delete confirm copy before trashing', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    vi.spyOn(filesApi, 'remove').mockResolvedValue(undefined as never)
    const w = mountDialog()
    await flushPromises()
    await (w.vm as unknown as { onDelete: () => Promise<void> }).onDelete()
    await flushPromises()
    // The dialog's delete action trashes (filesApi.remove defaults to soft-delete), so its confirm
    // copy must match -- not the hard-delete "cannot be undone" copy.
    expect(confirmRequire).toHaveBeenCalledWith(expect.objectContaining({
      header: 'Move to trash',
      message: 'Move this item to trash? You can restore it later.',
    }))
  })

  it('mounts no ConfirmDialog of its own', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    // A child dialog next to MediaLibraryView's own is exactly what used to fire one confirmation
    // twice; the store-backed host in AppShell is the only one now.
    expect(w.findComponent({ name: 'ConfirmDialog' }).exists()).toBe(false)
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

  it('passes the loaded folder tree to TreeSelect, not an empty array', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    // A regression that starves the picker (e.g. an empty array) would still render a TreeSelect
    // and still let the user pick Uncategorized, so the existence check above cannot catch it --
    // only the loaded rows actually reaching the child can.
    const nodes = w.findComponent({ name: 'TreeSelect' }).props('nodes') as { label: string; children?: { label: string }[] }[]
    const rootA = nodes.find((n) => n.label === 'Root A')
    expect(rootA).toBeTruthy()
    expect(rootA?.children?.[0]).toMatchObject({ label: 'Child B' })
  })

  it('gives the folder picker trigger an accessible name that includes the field label', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    // form/TreeSelect's aria-label overrides the trigger's visible text rather than supplementing
    // it, building it as "<label>: <value>" -- without the label prop it would announce only the
    // current value ("Uncategorized"), forgetting which field it belongs to.
    const trigger = w.findAll('button').find((b) => (b.attributes('aria-label') ?? '').startsWith('Folder:'))
    expect(trigger?.attributes('aria-label')).toBe('Folder: Uncategorized')
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

  it('passes the current folder key to TreeSelect as a plain key, not a keyed object', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ ...item, folder: { id: 'b' } } as never)
    const w = mountDialog()
    await flushPromises()
    expect(w.findComponent({ name: 'TreeSelect' }).props('modelValue')).toBe('b')
  })

  it('passes the Uncategorized key when the file is unfiled', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    expect(w.findComponent({ name: 'TreeSelect' }).props('modelValue')).toBe('__unfiled')
  })

  it('stores a folder pick emitted by the real TreeSelect child', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue(item as never)
    const w = mountDialog()
    await flushPromises()
    // Emitting from the vendored child runs this component's real @update:model-value listener,
    // which is the binding under test; a defineExpose call would bypass it entirely.
    await w.findComponent({ name: 'TreeSelect' }).vm.$emit('update:modelValue', 'b')
    await flushPromises()
    expect(w.findComponent({ name: 'TreeSelect' }).props('modelValue')).toBe('b')
    expect((w.vm as unknown as { folderId: string | null }).folderId).toBe('b')
  })

  it('maps the Uncategorized pick back to a null folderId', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ ...item, folder: { id: 'b' } } as never)
    const w = mountDialog()
    await flushPromises()
    await w.findComponent({ name: 'TreeSelect' }).vm.$emit('update:modelValue', '__unfiled')
    await flushPromises()
    expect((w.vm as unknown as { folderId: string | null }).folderId).toBeNull()
  })

  // Same file:A -> file:B transition as the vendored-dialog-and-inputs test above (both values
  // non-null, so the dialog itself never unmounts/remounts) -- here proving the same thing for
  // TreeSelect's folder binding instead of the File URL input.
  it('follows a new file prop to a different folder while the dialog stays open', async () => {
    vi.spyOn(itemsApi, 'get').mockImplementation((_collection, id) =>
      Promise.resolve(
        id === 'f1' ? { ...item, folder: { id: 'a' } } : { ...item, id: 'f2', folder: { id: 'b' } },
      ) as never,
    )
    const w = mountDialog()
    await flushPromises()
    expect(w.findComponent({ name: 'TreeSelect' }).props('modelValue')).toBe('a')
    await w.setProps({ file: { id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2048 } })
    await flushPromises()
    expect(w.findComponent({ name: 'TreeSelect' }).props('modelValue')).toBe('b')
  })

  it('sends the selected folder id in the update payload on save', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ ...item, folder: null } as never)
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({} as never)
    const w = mountDialog()
    await flushPromises()
    const vm = w.vm as unknown as { onFolderChange: (key: string | null) => void; onSave: () => Promise<void> }
    vm.onFolderChange('b')
    await vm.onSave()
    await flushPromises()
    expect(update).toHaveBeenCalledWith('file', 'f1', expect.objectContaining({ folderId: 'b' }))
  })

  it('sends folderId null when Uncategorized is selected on save', async () => {
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ ...item, folder: { id: 'b', name: 'Child B' } } as never)
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({} as never)
    const w = mountDialog()
    await flushPromises()
    const vm = w.vm as unknown as { onFolderChange: (key: string | null) => void; onSave: () => Promise<void> }
    vm.onFolderChange('__unfiled')
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
