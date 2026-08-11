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
  const twoOptions = [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }]

  it('renders one checkbox per option', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: [] }, ...opts })
    expect(w.findAll('[role="checkbox"]')).toHaveLength(2)
  })

  it('gives the group an accessible name since the label-for-field.name pairing in ItemForm.vue does not resolve to any of these checkboxes', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions, label: 'Regions' }), modelValue: [] }, ...opts })
    expect(w.find('[role="group"][aria-label="Regions"]').exists()).toBe(true)
  })

  // The whole behaviour of this field is array arithmetic, so it must be driven by real clicks:
  // asserting a synthesised $emit would test the test, not the component (constraint 9).
  it('appends the clicked option immutably', async () => {
    const before = ['a']
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: before }, ...opts })
    await w.findAll('[role="checkbox"]')[1].trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual([['a', 'b']])
    expect(before).toEqual(['a'])
  })

  it('removes an already-checked option', async () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: ['a', 'b'] }, ...opts })
    await w.findAll('[role="checkbox"]')[0].trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual([['b']])
  })

  it('treats a null model as an empty selection', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: null }, ...opts })
    expect(w.findAll('[role="checkbox"][aria-checked="true"]')).toHaveLength(0)
  })

  it('propagates disabled to every checkbox, not just the first', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: [], disabled: true }, ...opts })
    const boxes = w.findAll('[role="checkbox"]')
    expect(boxes).toHaveLength(2)
    for (const box of boxes) expect(box.attributes('disabled')).toBe('')
  })

  // Pins the template's own `@update:model-value` listener (constraint: "test the real binding, not
  // an exposed setter") rather than only the DOM-click path above, so deleting that one template
  // attribute — which would leave every click test above green if the click itself still toggled
  // native focus/aria state via reka defaults — cannot pass unnoticed.
  it('relays the toggled array when the vendored child emits, wiring the template listener itself', async () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: ['a'] }, ...opts })
    await w.findAllComponents({ name: 'Checkbox' })[1].vm.$emit('update:modelValue', true)
    expect(w.emitted('update:modelValue')?.[0]).toEqual([['a', 'b']])
  })
})
