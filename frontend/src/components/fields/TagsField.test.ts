import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import TagsField from './TagsField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }

describe('TagsField', () => {
  it('renders one row per tag with value + label inputs', () => {
    const w = mount(TagsField, {
      props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'tech' }, { value: 'ai', label: '人工智慧' }] },
      ...opts,
    })
    expect(w.findAll('.tag-row')).toHaveLength(2)
  })

  it('adds a blank row', async () => {
    const w = mount(TagsField, { props: { field: field({ interface: 'tags' }), modelValue: [] }, ...opts })
    await w.get('.tag-add').trigger('click')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([[{ value: '' }]])
  })

  it('removes a row immutably', async () => {
    const w = mount(TagsField, {
      props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a' }, { value: 'b' }] }, ...opts,
    })
    await w.findAll('.tag-remove')[0].trigger('click')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([[{ value: 'b' }]])
  })

  it('edits value and label immutably', async () => {
    const w = mount(TagsField, { props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a' }] }, ...opts })
    const inputs = w.findAll('.tag-row input')
    await inputs[0].setValue('tech')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([[{ value: 'tech' }]])
    await inputs[1].setValue('科技')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([[{ value: 'a', label: '科技' }]])
  })
})
