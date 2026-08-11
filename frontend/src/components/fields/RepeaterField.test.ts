import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { createI18n } from 'vue-i18n'
import RepeaterField from './RepeaterField.vue'
import type { FieldMeta } from '../../types/schema'
import en from '../../locales/en'
import zhTW from '../../locales/zh-TW'

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
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })
// A separate instance locked to zh-TW: fields.moveUp / fields.moveDown / fields.add / common.delete
// resolve to different text in each pack, which is the only way to prove these labels run through
// t() rather than a hardcoded English string that happens to read back identical to en's value.
const zhI18n = createI18n({ legacy: false, locale: 'zh-TW', fallbackLocale: 'zh-TW', messages: { 'zh-TW': zhTW } })
const opts = { global: { plugins: [PrimeVue, i18n] } }
const zhOpts = { global: { plugins: [PrimeVue, zhI18n] } }

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

  it('moves a row down', async () => {
    const w = mount(RepeaterField, {
      props: { field: repeater, modelValue: [{ question: 'q1', answer: 'a1' }, { question: 'q2', answer: 'a2' }] },
      ...opts,
    })
    await w.findAll('.repeater-down')[0].trigger('click') // move first row down
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

  // The row's move-up/move-down/remove controls are icon-only; without an accessible name a
  // screen reader announces only "button" for each of them. Mounted with a non-empty model so the
  // row controls actually exist — asserting over an empty list would pass vacuously.
  it('gives the icon-only row controls accessible names', () => {
    const w = mount(RepeaterField, { props: { field: repeater, modelValue: [{ question: 'a' }, { question: 'b' }] }, ...opts })
    expect(w.get('.repeater-up').attributes('aria-label')).toBeTruthy()
    expect(w.get('.repeater-remove').attributes('aria-label')).toBeTruthy()
  })

  // Asserted under zh-TW (not en) because comparing against the English string alone cannot
  // distinguish a localised lookup from a hardcoded English word that happens to read back the same.
  it('resolves every control accessible name from the localised i18n pack', () => {
    const w = mount(RepeaterField, { props: { field: repeater, modelValue: [{ question: 'a' }, { question: 'b' }] }, ...zhOpts })
    const ups = w.findAll('.repeater-up')
    const downs = w.findAll('.repeater-down')
    expect(ups[1].attributes('aria-label')).toBe(zhTW.fields.moveUp)
    expect(downs[0].attributes('aria-label')).toBe(zhTW.fields.moveDown)
    expect(w.get('.repeater-remove').attributes('aria-label')).toBe(zhTW.common.delete)
    expect(w.get('.repeater-add').attributes('aria-label')).toBe(zhTW.fields.add)
  })

  // Boundary logic, independent of the disabled/RBAC gate covered separately below: move-up on
  // the first row and move-down on the last row must stay unavailable no matter how many rows
  // exist, while every other row keeps both controls enabled.
  it('disables move-up on the first row and move-down on the last row only', () => {
    const w = mount(RepeaterField, {
      props: { field: repeater, modelValue: [{ question: 'q1' }, { question: 'q2' }, { question: 'q3' }] },
      ...opts,
    })
    const ups = w.findAll('.repeater-up')
    const downs = w.findAll('.repeater-down')
    expect(ups[0].attributes('disabled')).toBe('')
    expect(ups[1].attributes('disabled')).toBeUndefined()
    expect(ups[2].attributes('disabled')).toBeUndefined()
    expect(downs[0].attributes('disabled')).toBeUndefined()
    expect(downs[1].attributes('disabled')).toBeUndefined()
    expect(downs[2].attributes('disabled')).toBe('')
  })

  // A negative assertion alone (no PrimeVue markup) also passes for a hand-rolled control, so
  // assert the vendored button's real data-slot hook positively too, across every control in a
  // multi-row mount: move-up/move-down/remove per row, plus the add button.
  it('renders the vendored button data-slot hook on every control', () => {
    const w = mount(RepeaterField, { props: { field: repeater, modelValue: [{ question: 'a' }, { question: 'b' }] }, ...opts })
    expect(w.findAll('[data-slot="button"]')).toHaveLength(7) // 2 rows * 3 controls + 1 add
    expect(w.find('.p-button').exists()).toBe(false)
  })

  // Mounted with a non-empty model so the row buttons actually exist — a disabled assertion over
  // an empty list would pass vacuously without ever touching a real button.
  it('genuinely disables every row control and the add button', () => {
    const w = mount(RepeaterField, {
      props: { field: repeater, modelValue: [{ question: 'a' }, { question: 'b' }], disabled: true },
      ...opts,
    })
    const ups = w.findAll('.repeater-up')
    const downs = w.findAll('.repeater-down')
    const removes = w.findAll('.repeater-remove')
    expect(ups).toHaveLength(2)
    ups.forEach((b) => expect(b.attributes('disabled')).toBe(''))
    downs.forEach((b) => expect(b.attributes('disabled')).toBe(''))
    removes.forEach((b) => expect(b.attributes('disabled')).toBe(''))
    expect(w.get('.repeater-add').attributes('disabled')).toBe('')
  })

  // A post-mount prop change is the only assertion that distinguishes a prop-driven control from
  // one seeded once and then left alone; ItemFormView's "Reload latest" and the revisions drawer's
  // revert both replace the whole model after mount, so the rows must follow a model swap, not
  // just reflect it on the first render.
  it('reflects a post-mount model replacement', async () => {
    const w = mount(RepeaterField, { props: { field: repeater, modelValue: [{ question: 'q1', answer: 'a1' }] }, ...opts })
    expect(w.findAll('.repeater-row')).toHaveLength(1)
    await w.setProps({ modelValue: [{ question: 'q2', answer: 'a2' }, { question: 'q3', answer: 'a3' }] })
    expect(w.findAll('.repeater-row')).toHaveLength(2)
    const inputs = w.findAll('.repeater-row input')
    expect((inputs[0].element as HTMLInputElement).value).toBe('q2')
  })
})
