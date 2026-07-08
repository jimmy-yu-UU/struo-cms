import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import RepeaterField from './RepeaterField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'faqs', label: 'FAQs', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const repeater = field({
  interface: 'repeater',
  fields: [
    field({ name: 'question', label: 'Question', interface: 'text' }),
    field({ name: 'answer', label: 'Answer', interface: 'text' }),
  ] as FieldMeta[],
})
const opts = { global: { plugins: [PrimeVue] } }

describe('RepeaterField', () => {
  it('renders one card per row', () => {
    const w = mount(RepeaterField, {
      props: { field: repeater, modelValue: [{ question: 'q1', answer: 'a1' }, { question: 'q2', answer: 'a2' }] },
      ...opts,
    })
    expect(w.findAll('.repeater-row')).toHaveLength(2)
  })

  it('adds a blank row', async () => {
    const w = mount(RepeaterField, { props: { field: repeater, modelValue: [] }, ...opts })
    await w.get('.repeater-add').trigger('click')
    const emitted = w.emitted('update:modelValue')?.at(-1)?.[0] as unknown[]
    expect(emitted).toHaveLength(1)
    expect(w.findAll('.repeater-row')).toHaveLength(1)
  })

  it('removes a row immutably', async () => {
    const w = mount(RepeaterField, {
      props: { field: repeater, modelValue: [{ question: 'q1', answer: 'a1' }, { question: 'q2', answer: 'a2' }] },
      ...opts,
    })
    await w.findAll('.repeater-remove')[0].trigger('click')
    const emitted = w.emitted('update:modelValue')?.at(-1)?.[0] as Array<Record<string, unknown>>
    expect(emitted).toHaveLength(1)
    expect(emitted[0].question).toBe('q2')
  })

  it('moves a row up', async () => {
    const w = mount(RepeaterField, {
      props: { field: repeater, modelValue: [{ question: 'q1', answer: 'a1' }, { question: 'q2', answer: 'a2' }] },
      ...opts,
    })
    await w.findAll('.repeater-up')[1].trigger('click') // move second row up
    const emitted = w.emitted('update:modelValue')?.at(-1)?.[0] as Array<Record<string, unknown>>
    expect(emitted.map((r) => r.question)).toEqual(['q2', 'q1'])
  })

  it('edits a sub-field', async () => {
    const w = mount(RepeaterField, {
      props: { field: repeater, modelValue: [{ question: 'q1', answer: 'a1' }] }, ...opts,
    })
    const input = w.findAll('.repeater-row input')[0]
    await input.setValue('edited')
    const emitted = w.emitted('update:modelValue')?.at(-1)?.[0] as Array<Record<string, unknown>>
    expect(emitted[0].question).toBe('edited')
  })
})
