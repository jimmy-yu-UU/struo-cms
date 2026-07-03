import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import RelationPicker from './RelationPicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import type { RelationMeta, CollectionMeta } from '../../types/schema'

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
    const w = mount(RelationPicker, { props: { relation, modelValue: null }, global: { stubs } })
    await (w.vm as any).loadOptions()
    expect(listSpy).toHaveBeenCalledWith('category', expect.objectContaining({ page: 0 }))
    expect((w.vm as any).options).toHaveLength(1)
    expect((w.vm as any).options[0].label).toBe('Tech')
  })

  it('emits update:modelValue on single-select change', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const w = mount(RelationPicker, { props: { relation, modelValue: null }, global: { stubs } })
    ;(w.vm as any).onChange('c1')
    expect(w.emitted('update:modelValue')).toBeTruthy()
    expect(w.emitted('update:modelValue')![0]).toEqual(['c1'])
  })

  it('back-fills a preselected label via itemsApi.get when not present in options', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
    const getSpy = vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'c9', name: 'Archived' })
    const w = mount(RelationPicker, { props: { relation, modelValue: 'c9' }, global: { stubs } })
    await (w.vm as any).ensureSelectedLabels()
    expect(getSpy).toHaveBeenCalledWith('category', 'c9', expect.objectContaining({}))
  })
})
