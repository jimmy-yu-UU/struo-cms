import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import KeyValueField from './KeyValueField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }

describe('KeyValueField', () => {
  it('renders one row per entry', () => {
    const w = mount(KeyValueField, {
      props: { field: field({ interface: 'keyValue' }), modelValue: { a: '1', b: '2' } }, ...opts,
    })
    expect(w.findAll('.kv-row')).toHaveLength(2)
  })

  it('adds a blank row without emitting a blank key', async () => {
    const w = mount(KeyValueField, { props: { field: field({ interface: 'keyValue' }), modelValue: {} }, ...opts })
    await w.get('.kv-add').trigger('click')
    // A blank new row does not contribute a key yet -> object stays empty.
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([{}])
    expect(w.findAll('.kv-row')).toHaveLength(1)
  })

  it('emits an object once a key is typed', async () => {
    const w = mount(KeyValueField, { props: { field: field({ interface: 'keyValue' }), modelValue: {} }, ...opts })
    await w.get('.kv-add').trigger('click')
    const inputs = w.findAll('.kv-row input')
    await inputs[0].setValue('seo-title')
    await inputs[1].setValue('值')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([{ 'seo-title': '值' }])
  })

  it('removes a row immutably', async () => {
    const w = mount(KeyValueField, {
      props: { field: field({ interface: 'keyValue' }), modelValue: { a: '1', b: '2' } }, ...opts,
    })
    await w.findAll('.kv-remove')[0].trigger('click')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([{ b: '2' }])
  })
})
