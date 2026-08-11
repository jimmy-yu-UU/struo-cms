import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import PrimeVue from 'primevue/config'
import FieldInput from './FieldInput.vue'
import FilePicker from './FilePicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import type { FieldMeta } from '../../types/schema'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: {
    noFileSelected: 'No file selected', selectFile: 'Select', clear: 'Clear', selectAFile: 'Select a file',
    searchFiles: 'Search files…', loadFilesFailed: 'Failed to load files.',
  } } },
})

function field(over: Partial<FieldMeta> = {}): FieldMeta {
  return { name: 'f', label: 'F', interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const stubs = {
  Input: { template: '<input class="stub-text" />' },
  Textarea: { template: '<textarea class="stub-textarea" />' },
  NumberField: { template: '<input class="stub-number" />' },
  Checkbox: { template: '<input class="stub-checkbox" />' },
  DatePicker: { template: '<input class="stub-date" />' },
  Select: { template: '<div class="stub-select" role="combobox" />' },
  RadioGroup: { template: '<div class="stub-radio" />' },
}

describe('FieldInput', () => {
  it('renders InputText for text interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'text' }), modelValue: '' }, global: { stubs } })
    expect(w.find('.stub-text').exists()).toBe(true)
  })
  it('renders RichTextInput for richText', () => {
    const w = mount(FieldInput, {
      props: { field: field({ interface: 'richText' }), modelValue: '' },
      global: { stubs: { ...stubs, RichTextInput: { template: '<div class="stub-richtext" />' } } },
    })
    expect(w.find('.stub-richtext').exists()).toBe(true)
    expect(w.find('.stub-textarea').exists()).toBe(false)
  })
  it('renders Select for select interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'select', options: [{ value: 'a', label: 'A' }] }), modelValue: '' }, global: { stubs } })
    expect(w.find('.stub-select').exists()).toBe(true)
  })
  // This stub key matches components/form/DatePicker.vue's inferred component name (Vue infers it
  // from the filename), so it intercepts DateField's child the same way regardless of which
  // component that name resolves to.
  it('renders DatePicker for date interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'date' }), modelValue: null }, global: { stubs } })
    expect(w.find('.stub-date').exists()).toBe(true)
  })

  it('renders read-only display for unsupported interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'somethingNew' }), modelValue: '{}' }, global: { stubs } })
    expect(w.find('.readonly-field').exists()).toBe(true)
    expect(w.find('.stub-text').exists()).toBe(false)
  })

  it('binds maxlength on text inputs when the field declares one', () => {
    const w = mount(FieldInput, {
      props: { field: field({ interface: 'text', maxLength: 100 }), modelValue: '' },
      global: { plugins: [PrimeVue] },
    })
    expect(w.get('input').attributes('maxlength')).toBe('100')
  })

  it('omits maxlength when the field has none', () => {
    const w2 = mount(FieldInput, {
      props: { field: field({ interface: 'textarea', maxLength: null }), modelValue: '' },
      global: { plugins: [PrimeVue] },
    })
    expect(w2.get('textarea').attributes('maxlength')).toBeUndefined()
  })

  it('renders FilePicker for image interface and relays the value', async () => {
    setActivePinia(createPinia())
    const lang = useLanguageStore()
    lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 })
    const w = mount(FieldInput, {
      props: { field: field({ name: 'heroImageId', label: 'Hero', interface: 'image' }), modelValue: null },
      global: { plugins: [i18n], stubs: { ...stubs, Dialog: true, Button: true, MediaGrid: true } },
    })
    await flushPromises()
    const picker = w.findComponent(FilePicker)
    expect(picker.exists()).toBe(true)
    picker.vm.$emit('update:modelValue', 'f1')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['f1'])
  })
})
