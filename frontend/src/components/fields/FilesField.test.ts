import { describe, it, expect, vi, beforeEach } from 'vitest'
import { nextTick } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import FilesField from './FilesField.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import type { FieldMeta } from '../../types/schema'
import en from '../../locales/en'
import zhTW from '../../locales/zh-TW'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { common: { delete: 'Delete' }, fields: {
    noFilesSelected: 'No files selected', selectFiles: 'Select files', searchFiles: 'Search files…',
    done: 'Done', loadFilesFailed: 'Failed to load files.', moveUp: 'Move up', moveDown: 'Move down',
  } } },
})

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'gallery', label: 'Gallery', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}

const f1 = { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }
const f2 = { id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2 }
const f3 = { id: 'f3', fileName: 'c.png', contentType: 'image/png', size: 3 }

const stubs = { SortableList: true, Dialog: true, Button: true, Input: true, MediaGrid: true, FileThumbnail: true }

function setupStores() {
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
}

describe('FilesField', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('no longer renders PrimeVue OrderList', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    expect(w.findComponent({ name: 'OrderList' }).exists()).toBe(false)
  })

  it('resolves the model ids in order on mount', async () => {
    setupStores()
    // Return out of model order to prove the component re-orders to the model.
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f2, f1], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    expect((w.vm as unknown as { currentIds: () => string[] }).currentIds()).toEqual(['f1', 'f2'])
  })

  it('keeps a missing id as a raw-id fallback row', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1], total: 1 }) // f2 gone
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    expect((w.vm as unknown as { currentIds: () => string[] }).currentIds()).toEqual(['f1', 'f2'])
  })

  it('reorder emits the new id order', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    ;(w.vm as unknown as { onReorder: (v: unknown[]) => void }).onReorder([f2, f1])
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f2', 'f1']])
  })

  it('removeAt emits the shortened array immutably', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    ;(w.vm as unknown as { removeAt: (i: number) => void }).removeAt(0)
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f2']])
  })

  it('toggling an unselected file in the picker appends it last', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list')
    listSpy.mockResolvedValueOnce({ data: [f1], total: 1 })   // mount resolve
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1'] }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    listSpy.mockResolvedValueOnce({ data: [f1, f3], total: 2 }) // dialog options
    await (w.vm as unknown as { openDialog: () => Promise<void> }).openDialog()
    await flushPromises()
    ;(w.vm as unknown as { toggle: (id: string) => void }).toggle('f3')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f1', 'f3']])
  })

  it('debounces search-driven option reloads into a single request', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    // Empty model -> resolve() short-circuits with no list call on mount.
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: [] }, global: { plugins: [i18n], stubs } })
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

  it('toggling an already-selected file removes it', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    ;(w.vm as unknown as { toggle: (id: string) => void }).toggle('f1')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f2']])
  })

  // Button/SortableList are rendered for real here (unlike the shared `stubs`, which stub both
  // out) because the native `type` attribute only exists on the actual rendered <button>, not on
  // a generic stub. ItemForm.vue wraps every field in <form @submit.prevent>, so an untyped button
  // defaults to type="submit" and turns "Select files" / a row's remove / SortableList's own
  // reorder controls into a record save.
  it('gives every button an explicit type="button"', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, {
      props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] },
      global: { plugins: [i18n], stubs: { Dialog: true, MediaGrid: true, FileThumbnail: true } },
    })
    await flushPromises()
    const buttons = w.findAll('button')
    // Length assertion first: an empty findAll would make the loop below pass having asserted
    // nothing, silently losing coverage if the row/select controls were ever removed.
    expect(buttons.length).toBeGreaterThan(0)
    for (const button of buttons) expect(button.attributes('type')).toBe('button')
  })

  it('passes disabled through to the row and select-files controls', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, {
      props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'], disabled: true },
      global: { plugins: [i18n], stubs: { Dialog: true, MediaGrid: true, FileThumbnail: true } },
    })
    await flushPromises()
    const buttons = w.findAll('button')
    expect(buttons.length).toBeGreaterThan(0)
    for (const button of buttons) expect(button.attributes('disabled')).toBeDefined()
  })

  // A prop-value assertion made only against the initial render doesn't distinguish "this reads
  // the model prop" from "this seeded local state once and never looked again" -- ItemFormView's
  // 409 "reload latest" and the revisions drawer's revert both replace the whole record model well
  // after this component is already mounted, so the rendered row list has to keep following the
  // prop past that point too.
  it('re-resolves the rendered rows when the model changes after mount', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list')
    listSpy.mockResolvedValueOnce({ data: [f1], total: 1 })
    const w = mount(FilesField, {
      props: { field: field({ interface: 'files' }), modelValue: ['f1'] },
      global: { plugins: [i18n], stubs: { Dialog: true, MediaGrid: true } },
    })
    await flushPromises()
    expect(w.text()).toContain('a.png')

    listSpy.mockResolvedValueOnce({ data: [f2], total: 1 })
    await w.setProps({ modelValue: ['f2'] })
    await flushPromises()
    expect(w.text()).toContain('b.png')
    expect(w.text()).not.toContain('a.png')
  })

  // The tests above drive the dialog exclusively through defineExpose'd methods (openDialog,
  // toggle, search), which proves the handler bodies work but not that the template listeners are
  // still wired to the real controls -- deleting `v-model="search"` from the template would leave
  // every one of those tests green. This mounts the dialog subtree for real and emits from the
  // vendored Input child itself, which runs FilesField's actual template listener rather than an
  // exposed stand-in for it. Per the reka-floating-component pattern, Dialog's own portal is
  // itself named Teleport, colliding with VTU's `stubs: { teleport: true }` and dropping slot
  // content unless renderStubDefaultSlot is also set.
  describe('dialog content (rendered for real, not driven through defineExpose)', () => {
    const globalDialogContent = {
      plugins: [i18n],
      stubs: { Button: true, MediaGrid: true, FileThumbnail: true, teleport: true },
      renderStubDefaultSlot: true,
    }

    it("wires the search Input's real update:modelValue emit through to loadOptions", async () => {
      setupStores()
      const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1], total: 1 })
      const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: [] }, global: globalDialogContent })
      await (w.vm as unknown as { openDialog: () => Promise<void> }).openDialog()
      await flushPromises()
      listSpy.mockClear()
      vi.useFakeTimers()
      try {
        await w.findComponent({ name: 'Input' }).vm.$emit('update:modelValue', 'abc')
        await nextTick()
        expect(listSpy).not.toHaveBeenCalled() // debounced, same as the exposed-search test above
        await vi.advanceTimersByTimeAsync(300)
        expect(listSpy).toHaveBeenCalledTimes(1)
        expect(listSpy.mock.calls[0]?.[1]).toMatchObject({ search: 'abc' })
      } finally {
        vi.useRealTimers()
      }
    })
  })

  // Asserted under zh-TW, not English, per the rule that an English-string comparison can't tell
  // a real translation apart from a hardcoded one. Uses the app's real locale bundles (not a
  // hand-rolled message pack) so this pins the production string, not a test-only stand-in.
  it('shows the localized empty state under zh-TW', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const zhI18n = createI18n({ legacy: false, locale: 'zh-TW', fallbackLocale: 'zh-TW', messages: { 'zh-TW': zhTW } })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: [] }, global: { plugins: [zhI18n], stubs } })
    await flushPromises()
    expect(w.text()).toContain(zhTW.fields.noFilesSelected)
    expect(w.text()).not.toContain(en.fields.noFilesSelected)
  })
})
