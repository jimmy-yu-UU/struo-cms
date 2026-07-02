import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ItemForm from './ItemForm.vue'
import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', fields: [
  field('status', { sort: 1 }),
  field('title', { translatable: true, sort: 2 }),
]}
const locales: LanguageInfo[] = [
  { code: 'en', name: 'English', isDefault: true },
  { code: 'zh-TW', name: '繁中', isDefault: false },
]
const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: '' }, 'zh-TW': { title: '' } } }
const stubs = {
  FieldInput: { props: ['field', 'modelValue', 'disabled'], template: '<div class="field-input" :data-name="field.name" />' },
  Button: { props: ['label'], template: '<button :data-label="label" @click="$emit(\'click\')">{{ label }}</button>' },
  Tabs: { template: '<div><slot /></div>' },
  TabList: { template: '<div><slot /></div>' },
  Tab: { template: '<button class="tab"><slot /></button>' },
  TabPanels: { template: '<div><slot /></div>' },
  TabPanel: { template: '<div class="tab-panel"><slot /></div>' },
}

describe('ItemForm', () => {
  it('renders shared fields once and a tab per locale', () => {
    const w = mount(ItemForm, { props: { meta, model, locales, errors: {} }, global: { stubs } })
    expect(w.findAll('.tab')).toHaveLength(2)
    // 1 shared (status) + translatable title rendered per locale panel (2) = 3 FieldInputs
    expect(w.findAll('.field-input')).toHaveLength(3)
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
})
