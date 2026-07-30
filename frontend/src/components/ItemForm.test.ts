import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ItemForm from './ItemForm.vue'
import RelationInput from './fields/RelationInput.vue'
import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { itemForm: {
    relations: 'Relations', translatableBadge: 'Translatable',
    localeComplete: 'Has content', localeIncomplete: 'No content yet',
  } } },
})
type ItemFormProps = InstanceType<typeof ItemForm>['$props']
function mountForm(props: Record<string, unknown>, extraStubs: Record<string, unknown> = {}) {
  return mount(ItemForm, { props: props as unknown as ItemFormProps, global: { plugins: [i18n], stubs: { ...stubs, ...extraStubs } } })
}

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
  Tabs: { props: ['value'], emits: ['update:value'], template: '<div class="tabs"><slot /></div>' },
  TabList: { template: '<div><slot /></div>' },
  Tab: { props: ['value'], template: '<button class="tab" @click="$emit(\'click\')"><slot /></button>' },
  TabPanels: { template: '<div><slot /></div>' },
  TabPanel: { props: ['value'], template: '<div class="tab-panel"><slot /></div>' },
}

describe('ItemForm', () => {
  it('renders a tab per locale but mounts only the active locale panel body', () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    expect(w.findAll('.tab')).toHaveLength(2)
    // 1 shared (status) + translatable title for the ACTIVE locale only (1) = 2 FieldInputs
    expect(w.findAll('.field-input')).toHaveLength(2)
  })
  it('jumps the active tab to the default locale when validation errors appear', async () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    // move off the default locale, then surface an error
    ;(w.vm as unknown as { activeLocale: string }).activeLocale = 'zh-TW'
    await w.setProps({ errors: { title: 'Title is required.' } })
    expect((w.vm as unknown as { activeLocale: string }).activeLocale).toBe('en')
  })
  it('shows a server error banner', () => {
    const w = mountForm({ meta, model, locales, errors: {}, serverError: 'boom' })
    expect(w.find('.error').text()).toContain('boom')
  })
  it('emits submit on form submit (Enter/submit still works with no internal buttons)', async () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    await w.find('form').trigger('submit')
    expect(w.emitted('submit')).toBeTruthy()
  })
  it('no longer renders internal Save/Cancel buttons (actions moved to the page-head)', () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    expect(w.find('button[data-label="Save"]').exists()).toBe(false)
    expect(w.find('button[data-label="Cancel"]').exists()).toBe(false)
  })
  it('renders a translatable badge for translatable fields', () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    expect(w.find('.tr-badge').exists()).toBe(true)
  })
  it('shows a filled dot for locales with content and a hollow dot for empty locales', () => {
    const withContent: FormModel = { shared: { status: 'draft' },
      translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } }, relations: {} }
    const w = mountForm({ meta, model: withContent, locales, errors: {} })
    const dots = w.findAll('.dot')
    expect(dots).toHaveLength(2)          // one per locale
    expect(dots[0].classes()).not.toContain('off') // en has content
    expect(dots[1].classes()).toContain('off')     // zh-TW empty
  })
  it('gives each completeness dot an accessible label instead of hiding it', () => {
    const withContent: FormModel = { shared: { status: 'draft' },
      translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } }, relations: {} }
    const w = mountForm({ meta, model: withContent, locales, errors: {} })
    const dots = w.findAll('.dot')
    expect(dots[0].attributes('aria-hidden')).toBeUndefined()
    expect(dots[0].attributes('aria-label')).toBe('Has content')
    expect(dots[1].attributes('aria-label')).toBe('No content yet')
  })
  it('renders no dots when there is only one locale', () => {
    const w = mountForm({ meta, model, locales: [{ code: 'en', name: 'English', isDefault: true }], errors: {} })
    expect(w.findAll('.dot')).toHaveLength(0)
  })
  it('renders a RelationInput per relation in the shared section', () => {
    const relMeta: CollectionMeta = {
      name: 'article', label: 'Article', defaultDisplayField: null,
      fields: [field('status', { sort: 1 })],
      relations: [{ name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false }],
    }
    const relModel: FormModel = { shared: { status: 'draft' }, translations: {}, relations: { category: null } }
    const w = mountForm({ meta: relMeta, model: relModel, locales: [], errors: {} }, { RelationInput: true })
    expect(w.findComponent(RelationInput).exists()).toBe(true)
  })
})
