import { describe, it, expect, vi, beforeEach } from 'vitest'
import { nextTick } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import FilePicker from './FilePicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'

const rows = [{ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }]

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: {
    fields: {
      noFileSelected: 'No file selected', selectFile: 'Select', clear: 'Clear', selectAFile: 'Select a file',
      searchFiles: 'Search files…', loadFilesFailed: 'Failed to load files.',
    },
    media: { folderAll: 'All files', folderUncategorized: 'Uncategorized' },
  } },
})

const stubs = {
  Dialog: true,
  Button: true,
  Input: true,
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
    const w = mount(FilePicker, { props: { modelValue: 'f1', image: true }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    expect(w.text()).toContain('a.png')
  })

  it('clear emits null', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 })
    const w = mount(FilePicker, { props: { modelValue: 'f1' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    ;(w.vm as unknown as { clear: () => void }).clear()
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([null])
  })

  it('selecting a file emits its id and closes the dialog', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows, total: 1 })
    const w = mount(FilePicker, { props: { modelValue: null }, global: { plugins: [i18n], stubs } })
    await (w.vm as unknown as { openDialog: () => Promise<void> }).openDialog()
    await flushPromises()
    ;(w.vm as unknown as { onSelect: (id: string) => void }).onSelect('f1')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['f1'])
  })

  it('falls back to showing the id when the current file is gone', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'get').mockRejectedValue(new Error('404'))
    const w = mount(FilePicker, { props: { modelValue: 'ghost' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    expect(w.text()).toContain('ghost')
  })

  // The current-file resolution is driven off the modelValue prop, not local state seeded once at
  // mount — ItemFormView's 409 "reload latest" and the revisions drawer's revert both replace the
  // whole record model well after this component is already mounted.
  it('resolves the current file again after modelValue changes post-mount', async () => {
    setupStores()
    const getSpy = vi.spyOn(itemsApi, 'get')
    getSpy.mockResolvedValueOnce({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 })
    const w = mount(FilePicker, { props: { modelValue: 'f1' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    expect(w.text()).toContain('a.png')

    getSpy.mockResolvedValueOnce({ id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2 })
    await w.setProps({ modelValue: 'f2' })
    await flushPromises()
    expect(w.text()).toContain('b.png')
    expect(w.text()).not.toContain('a.png')
  })

  // Button is rendered for real here (unlike the shared `stubs`, which stub it out) because the
  // native `type` attribute only exists on the actual rendered <button>, not on a generic stub.
  // ItemForm.vue wraps every field in <form @submit.prevent>, so an untyped button defaults to
  // type="submit" and turns "Select a file" / "Clear" into a record save.
  it('gives every button an explicit type="button"', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 })
    const w = mount(FilePicker, {
      props: { modelValue: 'f1' },
      global: { plugins: [i18n], stubs: { Dialog: true, Input: true, MediaGrid: true } },
    })
    await flushPromises()
    const buttons = w.findAll('button')
    expect(buttons.length).toBeGreaterThan(0)
    for (const button of buttons) expect(button.attributes('type')).toBe('button')
  })

  it('passes disabled through to the action buttons', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 })
    const w = mount(FilePicker, {
      props: { modelValue: 'f1', disabled: true },
      global: { plugins: [i18n], stubs: { Dialog: true, Input: true, MediaGrid: true } },
    })
    await flushPromises()
    const buttons = w.findAll('button')
    expect(buttons.length).toBeGreaterThan(0)
    for (const button of buttons) expect(button.attributes('disabled')).toBeDefined()
  })

  it('debounces search-driven option reloads into a single request', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: rows, total: 1 })
    const w = mount(FilePicker, { props: { modelValue: null }, global: { plugins: [i18n], stubs } })
    await (w.vm as unknown as { openDialog: () => Promise<void> }).openDialog()
    await flushPromises()
    listSpy.mockClear()
    vi.useFakeTimers()
    try {
      ;(w.vm as any).search = 'a'
      await nextTick()
      ;(w.vm as any).search = 'ab'
      await nextTick()
      ;(w.vm as any).search = 'abc'
      await nextTick()
      expect(listSpy).not.toHaveBeenCalled()
      await vi.advanceTimersByTimeAsync(300)
      expect(listSpy).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it('ignores a stale load that resolves after a newer one (latest-wins)', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list')
    const w = mount(FilePicker, { props: { modelValue: null }, global: { plugins: [i18n], stubs } })
    let resolveStale!: (v: { data: unknown[]; total: number }) => void
    let resolveFresh!: (v: { data: unknown[]; total: number }) => void
    const stale = new Promise<{ data: unknown[]; total: number }>((r) => (resolveStale = r))
    const fresh = new Promise<{ data: unknown[]; total: number }>((r) => (resolveFresh = r))
    listSpy.mockReturnValueOnce(stale as never).mockReturnValueOnce(fresh as never)
    const vm = w.vm as unknown as { loadOptions: () => Promise<void>; files: unknown[] }
    const pStale = vm.loadOptions() // older token
    const pFresh = vm.loadOptions() // newer token supersedes
    resolveFresh({ data: [{ id: 'new', fileName: 'new.png', contentType: 'image/png', size: 1 }], total: 1 })
    await pFresh
    resolveStale({ data: [{ id: 'old', fileName: 'old.png', contentType: 'image/png', size: 1 }], total: 1 }) // stale resolves last
    await pStale
    await flushPromises()
    expect(vm.files).toHaveLength(1)
    expect((vm.files[0] as { id: string }).id).toBe('new')
  })

  describe('folder filter', () => {
    const folderRows = [{ id: 'a', name: 'Folder A', parentId: null }]

    function mockList(opts: { folders?: unknown[]; folderError?: boolean } = {}) {
      return vi.spyOn(itemsApi, 'list').mockImplementation(async (collection: string) => {
        if (collection === 'mediafolder') {
          if (opts.folderError) throw new Error('folder load failed')
          const data = opts.folders ?? folderRows
          return { data: data as never, total: data.length }
        }
        return { data: rows, total: rows.length }
      })
    }

    type Vm = { openDialog: () => Promise<void>; onFolderChange: (s: string | null) => void; loadOptions: () => Promise<void>; files: unknown[]; folders: unknown[] }

    it('takes a plain folder key, not PrimeVue TreeSelect\'s keyed object', async () => {
      setupStores()
      mockList()
      const w = mount(FilePicker, { props: { modelValue: null }, global: { plugins: [i18n], stubs } })
      await flushPromises()
      const vm = w.vm as unknown as { onFolderChange: (k: string | null) => void; folderSel: string }
      vm.onFolderChange('f1')
      await flushPromises()
      expect(vm.folderSel).toBe('f1')
    })

    it('sends no folder filter by default (__all): existing behavior unchanged', async () => {
      setupStores()
      const listSpy = mockList()
      const w = mount(FilePicker, { props: { modelValue: null }, global: { plugins: [i18n], stubs } })
      await (w.vm as unknown as Vm).openDialog()
      await flushPromises()
      const fileCall = listSpy.mock.calls.find(([collection]) => collection === 'file')
      expect(fileCall?.[1]).toMatchObject({ filter: undefined })
    })

    it('loads folders with deep:[\'parent\'] so nested folderId is actually populated', async () => {
      setupStores()
      const listSpy = mockList()
      const w = mount(FilePicker, { props: { modelValue: null }, global: { plugins: [i18n], stubs } })
      await (w.vm as unknown as Vm).openDialog()
      await flushPromises()
      const folderCall = listSpy.mock.calls.find(([collection]) => collection === 'mediafolder')
      expect(folderCall?.[1]).toMatchObject({ deep: ['parent'] })
    })

    it('filters by folderId when a folder is selected', async () => {
      setupStores()
      const listSpy = mockList()
      const w = mount(FilePicker, { props: { modelValue: null }, global: { plugins: [i18n], stubs } })
      await (w.vm as unknown as Vm).openDialog()
      await flushPromises()
      listSpy.mockClear()
      ;(w.vm as unknown as Vm).onFolderChange('a')
      await flushPromises()
      const fileCall = listSpy.mock.calls.find(([collection]) => collection === 'file')
      expect(fileCall?.[1]?.filter).toEqual({ folderId: { op: '_eq', value: 'a' } })
    })

    it('filters to uncategorized files when the Uncategorized node is selected', async () => {
      setupStores()
      const listSpy = mockList()
      const w = mount(FilePicker, { props: { modelValue: null }, global: { plugins: [i18n], stubs } })
      await (w.vm as unknown as Vm).openDialog()
      await flushPromises()
      listSpy.mockClear()
      ;(w.vm as unknown as Vm).onFolderChange('__unfiled')
      await flushPromises()
      const fileCall = listSpy.mock.calls.find(([collection]) => collection === 'file')
      expect(fileCall?.[1]?.filter).toEqual({ folderId: { op: '_null', value: 'true' } })
    })

    it('still loads files when folder loading fails (folder filter degrades away)', async () => {
      setupStores()
      const listSpy = mockList({ folderError: true })
      const w = mount(FilePicker, { props: { modelValue: null }, global: { plugins: [i18n], stubs } })
      await (w.vm as unknown as Vm).openDialog()
      await flushPromises()
      const vm = w.vm as unknown as Vm
      expect(vm.files).toHaveLength(rows.length)
      expect(vm.folders).toHaveLength(0)
      expect(listSpy.mock.calls.some(([collection]) => collection === 'file')).toBe(true)
    })

    it('composes search and folder filters into the same request', async () => {
      setupStores()
      const listSpy = mockList()
      const w = mount(FilePicker, { props: { modelValue: null }, global: { plugins: [i18n], stubs } })
      await (w.vm as unknown as Vm).openDialog()
      await flushPromises()
      ;(w.vm as unknown as Vm).onFolderChange('a')
      await flushPromises()
      listSpy.mockClear()
      ;(w.vm as unknown as { search: string }).search = 'x'
      await (w.vm as unknown as Vm).loadOptions()
      await flushPromises()
      const fileCall = listSpy.mock.calls.find(([collection]) => collection === 'file')
      expect(fileCall?.[1]?.search).toBe('x')
      expect(fileCall?.[1]?.filter).toEqual({ folderId: { op: '_eq', value: 'a' } })
    })
  })
})
