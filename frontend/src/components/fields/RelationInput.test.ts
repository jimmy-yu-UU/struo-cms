import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import RelationInput from './RelationInput.vue'
import RelationPicker from './RelationPicker.vue'
import RelatedList from './RelatedList.vue'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import type { RelationMeta, CollectionMeta } from '../../types/schema'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { loadOptionsFailed: 'Failed to load options.', noRelatedItems: 'No related items.' } } },
})

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

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

function rel(overrides: Partial<RelationMeta> = {}): RelationMeta {
  return {
    name: 'r',
    label: 'R',
    kind: 'manyToOne',
    targetCollection: 'category',
    interface: 'dropdown',
    foreignKey: 'CategoryId',
    displayTemplate: '{Name}',
    editable: true,
    selfReferencing: false,
    ...overrides,
  }
}

const stubs = {
  Combobox: true,
  TreeSelect: true,
  DataTable: true,
  DataTablePagination: true,
}

function setupStores() {
  const schema = useSchemaStore()
  schema.get = vi.fn().mockReturnValue(targetMeta) as never
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  return { schema, lang }
}

describe('RelationInput', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    push.mockReset()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 })
  })

  it('dispatches dropdown interface to RelationPicker', () => {
    setupStores()
    const w = mount(RelationInput, {
      props: { relation: rel({ interface: 'dropdown' }), modelValue: null },
      global: { plugins: [i18n], stubs },
    })
    expect(w.findComponent(RelationPicker).exists()).toBe(true)
  })

  it('dispatches tagSelect interface to RelationPicker as multiple', () => {
    setupStores()
    const w = mount(RelationInput, {
      props: { relation: rel({ interface: 'tagSelect' }), modelValue: null },
      global: { plugins: [i18n], stubs },
    })
    const picker = w.findComponent(RelationPicker)
    expect(picker.exists()).toBe(true)
    expect(picker.props('multiple')).toBe(true)
  })

  it('dispatches treeSelect interface to RelationPicker as tree with excludeId', () => {
    setupStores()
    const w = mount(RelationInput, {
      props: { relation: rel({ interface: 'treeSelect', selfReferencing: true }), modelValue: null, excludeId: 'x1' },
      global: { plugins: [i18n], stubs },
    })
    const picker = w.findComponent(RelationPicker)
    expect(picker.exists()).toBe(true)
    expect(picker.props('tree')).toBe(true)
    expect(picker.props('excludeId')).toBe('x1')
  })

  it('dispatches relatedList interface to RelatedList', () => {
    setupStores()
    const w = mount(RelationInput, {
      props: {
        relation: rel({ interface: 'relatedList', kind: 'oneToMany', editable: false }),
        modelValue: null,
        parentId: 'p1',
      },
      global: { plugins: [i18n], stubs },
    })
    expect(w.findComponent(RelatedList).exists()).toBe(true)
  })

  it('falls back to read-only span for an unknown interface', () => {
    setupStores()
    const w = mount(RelationInput, {
      props: { relation: rel({ interface: 'filePicker' }), modelValue: null },
      global: { plugins: [i18n], stubs },
    })
    expect(w.findComponent(RelationPicker).exists()).toBe(false)
    expect(w.findComponent(RelatedList).exists()).toBe(false)
    expect(w.find('.readonly-relation').exists()).toBe(true)
  })
})
