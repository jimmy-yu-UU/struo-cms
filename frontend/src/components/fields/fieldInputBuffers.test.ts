import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import NumberField from './NumberField.vue'
import DateField from './DateField.vue'
import TagsField from './TagsField.vue'
import KeyValueField from './KeyValueField.vue'
import RepeaterField from './RepeaterField.vue'
import { ALL_FIELD_INTERFACES } from '../../lib/fieldTypes/types'
import { registry } from '../../lib/fieldTypes/registry'
import { catalogs } from '../../locales'
import type { FieldMeta } from '../../types/schema'

// Inventory of the field components that keep text of their own between keystrokes and the model.
// Each case measures when the model is told: a component that tells it on every input, or before the
// form can read it, has no unparsable state to report. Only JSON owns an input buffer (JsonField.test.ts).

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: catalogs as never })

function field(iface: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name: 'f', label: 'F', interface: iface, required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const global = { plugins: [i18n] }

describe('which field types own their input', () => {
  it('flags json and nothing else', () => {
    const owners = ALL_FIELD_INTERFACES.filter((i) => registry[i].ownsInput === true)
    expect(owners).toEqual(['json'])
  })
})

describe('NumberField', () => {
  it('tells the model on Enter and on blur, not on each input event', async () => {
    const w = mount(NumberField, { props: { field: field('number'), modelValue: 5 }, global, attachTo: document.body })
    const input = w.get('input')
    await input.setValue('12')
    expect(w.emitted('update:modelValue')).toBeUndefined()
    await input.trigger('keydown', { key: 'Enter' })
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([12])
    await w.setProps({ modelValue: 12 })
    await input.setValue('34')
    await input.trigger('blur')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([34])
  })

  it('turns text that is not a number into an empty value on blur, never a stale one', async () => {
    const w = mount(NumberField, { props: { field: field('number'), modelValue: 5 }, global, attachTo: document.body })
    await w.get('input').setValue('abc')
    await w.get('input').trigger('blur')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([null])
  })
})

describe('DateField time input', () => {
  it('tells the model on every input event', async () => {
    const w = mount(DateField, { props: { field: field('time'), modelValue: null }, global })
    await w.get('input[type="time"]').setValue('10:30')
    const date = w.emitted('update:modelValue')?.at(-1)?.[0] as Date
    expect([date.getHours(), date.getMinutes()]).toEqual([10, 30])
  })

  it('clears a time-only field when the time is cleared', async () => {
    const w = mount(DateField, { props: { field: field('time'), modelValue: new Date(2026, 0, 2, 9, 5) }, global })
    await w.get('input[type="time"]').setValue('')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([null])
  })

  it('renders no text input for the date part, only the calendar trigger', () => {
    const w = mount(DateField, { props: { field: field('date'), modelValue: null }, global })
    expect(w.findAll('input')).toHaveLength(0)
  })
})

describe('multi-value fields', () => {
  it('TagsField tells the model on every keystroke', async () => {
    const w = mount(TagsField, { props: { field: field('tags'), modelValue: [{ value: 'a' }] }, global })
    await w.get('input').setValue('ab')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([[{ value: 'ab' }]])
  })

  it('KeyValueField tells the model on every keystroke', async () => {
    const w = mount(KeyValueField, { props: { field: field('keyValue'), modelValue: { k: 'v' } }, global })
    await w.findAll('input')[1].setValue('vv')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([{ k: 'vv' }])
  })

  it('RepeaterField tells the model on every keystroke in a text sub-field', async () => {
    const sub = field('text', { name: 'q', label: 'Q' })
    const w = mount(RepeaterField, {
      props: { field: field('repeater', { fields: [sub] }), modelValue: [{ q: 'a' }] }, global,
    })
    await w.get('input').setValue('ab')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([[{ q: 'ab' }]])
  })
})
