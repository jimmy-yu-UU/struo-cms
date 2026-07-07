import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import MultiSelectField from './MultiSelectField.vue'
import CheckboxGroupField from './CheckboxGroupField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }
const options = [{ value: 'apac', label: 'APAC' }, { value: 'emea', label: 'EMEA' }]

describe('MultiSelectField', () => {
  it('passes options and value to PrimeVue MultiSelect', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: ['apac'] }, ...opts })
    const ms = w.findComponent({ name: 'MultiSelect' })
    expect(ms.props('options')).toEqual(options)
    expect(ms.props('modelValue')).toEqual(['apac'])
  })

  it('relays selection changes as an array', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: [] }, ...opts })
    w.findComponent({ name: 'MultiSelect' }).vm.$emit('update:modelValue', ['apac', 'emea'])
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['apac', 'emea']])
  })
})

describe('CheckboxGroupField', () => {
  it('renders one checkbox per option', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options }), modelValue: [] }, ...opts })
    expect(w.findAll('.checkbox-option')).toHaveLength(2)
  })

  it('relays the toggled array', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options }), modelValue: ['apac'] }, ...opts })
    w.findComponent({ name: 'Checkbox' }).vm.$emit('update:modelValue', ['apac', 'emea'])
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['apac', 'emea']])
  })
})
