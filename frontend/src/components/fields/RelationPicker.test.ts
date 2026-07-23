import { describe, it, expect, vi, beforeEach } from 'vitest'
import { nextTick } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import RelationPicker from './RelationPicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import type { RelationMeta, CollectionMeta } from '../../types/schema'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { loadOptionsFailed: 'Failed to load options.' } } },
})

const targetMeta: CollectionMeta = {
  name: 'category',
  label: 'Category',
  defaultDisplayField: 'name',
  fields: [
    { name: 'name', label: 'Name', interface: 'text', required: false, searchable: false, sortable: false,
      readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false },
  ],
  relations: [],
}

const relation: RelationMeta = {
  name: 'category',
  label: 'Category',
  kind: 'manyToOne',
  targetCollection: 'category',
  interface: 'dropdown',
  foreignKey: 'CategoryId',
  displayTemplate: null,
  editable: true,
  selfReferencing: false,
}

const stubs = {
  Select: true,
  MultiSelect: true,
  TreeSelect: true,
}

function setupStores() {
  const schema = useSchemaStore()
  schema.get = vi.fn().mockReturnValue(targetMeta) as never
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  return { schema, lang }
}

describe('RelationPicker', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('lazy-loads options via itemsApi.list and resolves labels', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [{ id: 'c1', name: 'Tech' }], total: 1 })
    const w = mount(RelationPicker, { props: { relation, modelValue: null }, global: { plugins: [i18n], stubs } })
    await (w.vm as any).loadOptions()
    expect(listSpy).toHaveBeenCalledWith('category', expect.objectContaining({ page: 0 }))
    expect((w.vm as any).options).toHaveLength(1)
    expect((w.vm as any).options[0].label).toBe('Tech')
  })

  it('emits update:modelValue on single-select change', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelationPicker, { props: { relation, modelValue: null }, global: { plugins: [i18n], stubs } })
    ;(w.vm as any).onChange('c1')
    expect(w.emitted('update:modelValue')).toBeTruthy()
    expect(w.emitted('update:modelValue')![0]).toEqual(['c1'])
  })

  it('debounces search-driven reloads into a single request', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelationPicker, { props: { relation, modelValue: null }, global: { plugins: [i18n], stubs } })
    await flushPromises() // mount load settles under real timers
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

  it('back-fills a preselected label via itemsApi.get when not present in options', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const getSpy = vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'c9', name: 'Archived' })
    const w = mount(RelationPicker, { props: { relation, modelValue: 'c9' }, global: { plugins: [i18n], stubs } })
    await (w.vm as any).ensureSelectedLabels()
    expect(getSpy).toHaveBeenCalledWith('category', 'c9', expect.objectContaining({}))
  })

  it('merges a preselected-but-absent id into displayOptions using labelById', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'c9', name: 'Archived' })
    const w = mount(RelationPicker, { props: { relation, modelValue: 'c9' }, global: { plugins: [i18n], stubs } })
    await flushPromises() // mount: loadOptions (empty) + ensureSelectedLabels settle
    const disp = (w.vm as any).displayOptions
    expect(disp).toHaveLength(1)
    expect(disp[0]).toEqual({ id: 'c9', label: 'Archived', raw: {} })
  })

  it('ignores a stale load that resolves after a newer one (latest-wins)', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelationPicker, { props: { relation, modelValue: null }, global: { plugins: [i18n], stubs } })
    await flushPromises() // let the mount load settle first
    let resolveStale!: (v: { data: Record<string, unknown>[]; total: number }) => void
    let resolveFresh!: (v: { data: Record<string, unknown>[]; total: number }) => void
    const stale = new Promise<{ data: Record<string, unknown>[]; total: number }>((r) => (resolveStale = r))
    const fresh = new Promise<{ data: Record<string, unknown>[]; total: number }>((r) => (resolveFresh = r))
    listSpy.mockReturnValueOnce(stale as never).mockReturnValueOnce(fresh as never)
    const pStale = (w.vm as any).loadOptions() // older token
    const pFresh = (w.vm as any).loadOptions() // newer token supersedes
    resolveFresh({ data: [{ id: 'new', name: 'New' }], total: 1 })
    await pFresh
    resolveStale({ data: [{ id: 'old', name: 'Old' }], total: 1 }) // stale resolves last
    await pStale
    await flushPromises()
    expect((w.vm as any).options).toHaveLength(1)
    expect((w.vm as any).options[0].id).toBe('new')
  })
})
