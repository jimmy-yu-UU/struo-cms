import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import JsonField from './JsonField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }

describe('JsonField', () => {
  it('initialises the textarea from the model value (pretty-printed)', () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: { a: 1 } }, ...opts })
    const ta = w.find('textarea')
    expect((ta.element as HTMLTextAreaElement).value).toContain('"a": 1')
  })

  it('emits the parsed value on valid JSON input', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: null }, ...opts })
    await w.find('textarea').setValue('{"x": [1, 2]}')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([{ x: [1, 2] }])
  })

  it('emits null on a blank buffer', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: { a: 1 } }, ...opts })
    await w.find('textarea').setValue('   ')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([null])
  })

  it('does not emit and shows an error on malformed JSON', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: null }, ...opts })
    await w.find('textarea').setValue('{ not json')
    expect(w.emitted('update:modelValue')).toBeUndefined()
    expect(w.find('.json-error').exists()).toBe(true)
  })
})
