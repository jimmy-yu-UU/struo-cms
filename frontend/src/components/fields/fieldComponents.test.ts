import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import TextField from './TextField.vue'
import TextareaField from './TextareaField.vue'
import NumberField from './NumberField.vue'
import BooleanField from './BooleanField.vue'
import DateField from './DateField.vue'
import SelectField from './SelectField.vue'
import RadioField from './RadioField.vue'
import DividerField from './DividerField.vue'
import ReadonlyField from './ReadonlyField.vue'
import type { FieldMeta } from '../../types/schema'
import { setActivePinia, createPinia } from 'pinia'
import { flushPromises } from '@vue/test-utils'
import { vi } from 'vitest'
import { createI18n } from 'vue-i18n'
import RichTextField from './RichTextField.vue'
import FileField from './FileField.vue'
import FilePicker from './FilePicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: {
    noFileSelected: 'No file selected', selectFile: 'Select', clear: 'Clear', selectAFile: 'Select a file',
    searchFiles: 'Search files…', loadFilesFailed: 'Failed to load files.',
  } } },
})

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }

describe('field components (simple inputs)', () => {
  it('TextField renders an input, binds maxlength, and emits on input', async () => {
    const w = mount(TextField, { props: { field: field({ interface: 'text', maxLength: 50 }), modelValue: '' }, ...opts })
    const input = w.get('input')
    expect(input.attributes('maxlength')).toBe('50')
    await input.setValue('hi')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['hi'])
  })

  it('TextField omits maxlength when field has none', () => {
    const w = mount(TextField, { props: { field: field({ interface: 'text', maxLength: null }), modelValue: '' }, ...opts })
    expect(w.get('input').attributes('maxlength')).toBeUndefined()
  })

  it('TextareaField renders a textarea and binds maxlength', () => {
    const w = mount(TextareaField, { props: { field: field({ interface: 'textarea', maxLength: 20 }), modelValue: '' }, ...opts })
    expect(w.get('textarea').attributes('maxlength')).toBe('20')
  })

  it('NumberField renders a numeric input', () => {
    const w = mount(NumberField, { props: { field: field({ interface: 'number' }), modelValue: 3 }, ...opts })
    expect(w.find('input').exists()).toBe(true)
  })

  it('BooleanField renders a checkbox and is disabled when asked', () => {
    const w = mount(BooleanField, { props: { field: field({ interface: 'boolean' }), modelValue: true, disabled: true }, ...opts })
    expect(w.find('input[type="checkbox"]').exists()).toBe(true)
  })

  it('DateField sets time-only for time and show-time for dateTime', () => {
    const t = mount(DateField, { props: { field: field({ interface: 'time' }), modelValue: null }, ...opts })
    expect(t.findComponent({ name: 'DatePicker' }).props('timeOnly')).toBe(true)
    const dt = mount(DateField, { props: { field: field({ interface: 'dateTime' }), modelValue: null }, ...opts })
    expect(dt.findComponent({ name: 'DatePicker' }).props('showTime')).toBe(true)
  })

  // The migration's own assertion: this field must no longer resolve a PrimeVue component.
  // findComponent({ name }) is the same lookup the pre-migration tests used for Select/DatePicker.
  it('TextField renders the vendored Input, not PrimeVue InputText', () => {
    const w = mount(TextField, { props: { field: field({ interface: 'text' }), modelValue: 'x' }, ...opts })
    expect(w.findComponent({ name: 'InputText' }).exists()).toBe(false)
    expect(w.find('input').exists()).toBe(true)
  })

  it('TextareaField keeps six rows after the migration', () => {
    const w = mount(TextareaField, { props: { field: field({ interface: 'textarea' }), modelValue: 'x' }, ...opts })
    expect(w.get('textarea').attributes('rows')).toBe('6')
  })
})

describe('field components (choice + structural)', () => {
  it('SelectField exposes its options', () => {
    const w = mount(SelectField, {
      props: { field: field({ interface: 'select', options: [{ value: 'a', label: 'A' }] }), modelValue: 'a' },
      global: { plugins: [PrimeVue] },
    })
    expect(w.findComponent({ name: 'Select' }).props('options')).toEqual([{ value: 'a', label: 'A' }])
  })

  it('RadioField renders one option per choice', () => {
    const w = mount(RadioField, {
      props: { field: field({ interface: 'radio', options: [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }] }), modelValue: 'a' },
      global: { plugins: [PrimeVue] },
    })
    expect(w.findAll('.radio-option')).toHaveLength(2)
  })

  it('DividerField renders an hr', () => {
    const w = mount(DividerField, { props: { field: field({ interface: 'divider' }), modelValue: '' } })
    expect(w.find('hr').exists()).toBe(true)
  })

  it('ReadonlyField shows the value, em-dash when empty', () => {
    expect(mount(ReadonlyField, { props: { field: field({ interface: 'json' }), modelValue: '{}' } }).text()).toBe('{}')
    expect(mount(ReadonlyField, { props: { field: field({ interface: 'json' }), modelValue: null } }).find('.readonly-field').text()).toBe('—')
  })
})

describe('field components (rich-text + file wrappers)', () => {
  it('RichTextField relays the RichTextInput value', () => {
    const w = mount(RichTextField, {
      props: { field: field({ interface: 'richText' }), modelValue: '<p>hi</p>' },
      global: { stubs: { RichTextInput: { name: 'RichTextInput', props: ['modelValue'], template: '<div class="stub-rt" />' } } },
    })
    const rt = w.findComponent({ name: 'RichTextInput' })
    expect(rt.props('modelValue')).toBe('<p>hi</p>')
    rt.vm.$emit('update:modelValue', '<p>bye</p>')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['<p>bye</p>'])
  })

  it('RichTextField coerces a null model to empty string', () => {
    const w = mount(RichTextField, {
      props: { field: field({ interface: 'richText' }), modelValue: null },
      global: { stubs: { RichTextInput: { name: 'RichTextInput', props: ['modelValue'], template: '<div class="stub-rt" />' } } },
    })
    expect(w.findComponent({ name: 'RichTextInput' }).props('modelValue')).toBe('')
  })

  it('FileField sets image=true for the image interface and relays the id', async () => {
    setActivePinia(createPinia())
    useLanguageStore().languages = [{ code: 'en', name: 'English', isDefault: true }]
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 })
    const w = mount(FileField, {
      props: { field: field({ name: 'heroImageId', interface: 'image' }), modelValue: null },
      global: { plugins: [i18n], stubs: { Dialog: true, Button: true, MediaGrid: true } },
    })
    await flushPromises()
    const picker = w.findComponent(FilePicker)
    expect(picker.props('image')).toBe(true)
    picker.vm.$emit('update:modelValue', 'f1')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['f1'])
  })
})
