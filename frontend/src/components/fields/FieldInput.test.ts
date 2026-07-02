import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import FieldInput from './FieldInput.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> = {}): FieldMeta {
  return { name: 'f', label: 'F', interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const stubs = {
  InputText: { template: '<input class="stub-text" />' },
  Textarea: { template: '<textarea class="stub-textarea" />' },
  InputNumber: { template: '<input class="stub-number" />' },
  Checkbox: { template: '<input class="stub-checkbox" />' },
  DatePicker: { template: '<input class="stub-date" />' },
  Select: { template: '<select class="stub-select" />' },
  RadioButton: { template: '<input class="stub-radio" />' },
}

describe('FieldInput', () => {
  it('renders InputText for text interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'text' }), modelValue: '' }, global: { stubs } })
    expect(w.find('.stub-text').exists()).toBe(true)
  })
  it('renders Textarea for richText (fallback)', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'richText' }), modelValue: '' }, global: { stubs } })
    expect(w.find('.stub-textarea').exists()).toBe(true)
  })
  it('renders Select for select interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'select', options: [{ value: 'a', label: 'A' }] }), modelValue: '' }, global: { stubs } })
    expect(w.find('.stub-select').exists()).toBe(true)
  })
  it('renders read-only display for unsupported interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'json' }), modelValue: '{}' }, global: { stubs } })
    expect(w.find('.readonly-field').exists()).toBe(true)
    expect(w.find('.stub-text').exists()).toBe(false)
  })
})
