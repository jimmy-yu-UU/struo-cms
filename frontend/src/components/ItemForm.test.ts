import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ItemForm from './ItemForm.vue'
import RelationInput from './fields/RelationInput.vue'
import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', fields: [
  field('status', { sort: 1 }),
  field('title', { translatable: true, sort: 2 }),
], relations: [] }
const locales: LanguageInfo[] = [
  { code: 'en', name: 'English', isDefault: true },
  { code: 'zh-TW', name: '繁中', isDefault: false },
]
const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: '' }, 'zh-TW': { title: '' } }, relations: {} }
const stubs = {
  FieldInput: { props: ['field', 'modelValue', 'disabled'], template: '<div class="field-input" :data-name="field.name" />' },
  Button: { props: ['label'], template: '<button :data-label="label" @click="$emit(\'click\')">{{ label }}</button>' },
  Tabs: { props: ['value'], emits: ['update:value'], template: '<div class="tabs"><slot /></div>' },
  TabList: { template: '<div><slot /></div>' },
  Tab: { props: ['value'], template: '<button class="tab" @click="$emit(\'click\')"><slot /></button>' },
  TabPanels: { template: '<div><slot /></div>' },
  TabPanel: { props: ['value'], template: '<div class="tab-panel"><slot /></div>' },
}

describe('ItemForm', () => {
  it('renders a tab per locale but mounts only the active locale panel body', () => {
    const w = mount(ItemForm, { props: { meta, model, locales, errors: {} }, global: { stubs } })
    expect(w.findAll('.tab')).toHaveLength(2)
    // 1 shared (status) + translatable title for the ACTIVE locale only (1) = 2 FieldInputs
    expect(w.findAll('.field-input')).toHaveLength(2)
  })
  it('jumps the active tab to the default locale when validation errors appear', async () => {
    const w = mount(ItemForm, { props: { meta, model, locales, errors: {} }, global: { stubs } })
    // move off the default locale, then surface an error
    ;(w.vm as unknown as { activeLocale: string }).activeLocale = 'zh-TW'
    await w.setProps({ errors: { title: 'Title is required.' } })
    expect((w.vm as unknown as { activeLocale: string }).activeLocale).toBe('en')
  })
  it('shows a server error banner', () => {
    const w = mount(ItemForm, { props: { meta, model, locales, errors: {}, serverError: 'boom' }, global: { stubs } })
    expect(w.find('.error').text()).toContain('boom')
  })
  it('emits submit on form submit and cancel on Cancel click', async () => {
    const w = mount(ItemForm, { props: { meta, model, locales, errors: {} }, global: { stubs } })
    await w.find('form').trigger('submit')
    expect(w.emitted('submit')).toBeTruthy()
    await w.find('button[data-label="Cancel"]').trigger('click')
    expect(w.emitted('cancel')).toBeTruthy()
  })
  it('hides Save when disabled (read-only)', () => {
    const w = mount(ItemForm, { props: { meta, model, locales, errors: {}, disabled: true }, global: { stubs } })
    expect(w.find('button[data-label="Save"]').exists()).toBe(false)
  })
  it('renders a RelationInput per relation in the shared section', () => {
    const relMeta: CollectionMeta = {
      name: 'article', label: 'Article', defaultDisplayField: null,
      fields: [field('status', { sort: 1 })],
      relations: [{ name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false }],
    }
    const relModel: FormModel = { shared: { status: 'draft' }, translations: {}, relations: { category: null } }
    const w = mount(ItemForm, {
      props: { meta: relMeta, model: relModel, locales: [], errors: {} },
      global: { stubs: { ...stubs, RelationInput: true } },
    })
    expect(w.findComponent(RelationInput).exists()).toBe(true)
  })
})
