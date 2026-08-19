import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import KeyValueField from './KeyValueField.vue'
import type { FieldMeta } from '../../types/schema'
import en from '../../locales/en'
import zhTW from '../../locales/zh-TW'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })
// A separate instance locked to zh-TW: fields.add / common.delete resolve to different text in
// each pack, which is the only way to prove these labels run through t() rather than a hardcoded
// English string that happens to read back identical to en's current value.
const zhI18n = createI18n({ legacy: false, locale: 'zh-TW', fallbackLocale: 'zh-TW', messages: { 'zh-TW': zhTW } })
const opts = { global: { plugins: [i18n] } }
const zhOpts = { global: { plugins: [zhI18n] } }

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

  it('gives the icon-only remove control and the add control a resolved, localised accessible name', () => {
    const w = mount(KeyValueField, { props: { field: field({ interface: 'keyValue' }), modelValue: { a: '1' } }, ...zhOpts })
    expect(w.get('.kv-remove').attributes('aria-label')).toBe(zhTW.common.delete)
    expect(w.get('.kv-add').text()).toContain(zhTW.fields.add)
  })

  // Without a distinct accessible name, both inputs are unnamed native text boxes — a screen
  // reader announces "edit text" twice per row with no way to tell the key cell from the value
  // cell. Asserted under zh-TW (not en) because comparing against the English string alone cannot
  // distinguish a localised lookup from a hardcoded English word that happens to read back the same.
  it('gives the key and value inputs distinct, resolved, localised accessible names and placeholders', () => {
    const w = mount(KeyValueField, { props: { field: field({ interface: 'keyValue' }), modelValue: { a: '1' } }, ...zhOpts })
    const [keyInput, valueInput] = w.findAll('.kv-row input')
    expect(keyInput.attributes('aria-label')).toBe(zhTW.fields.keyValueKey)
    expect(valueInput.attributes('aria-label')).toBe(zhTW.fields.keyValueValue)
    expect(keyInput.attributes('aria-label')).not.toBe(valueInput.attributes('aria-label'))
    expect(keyInput.attributes('placeholder')).toBe(zhTW.fields.keyValueKey)
    expect(valueInput.attributes('placeholder')).toBe(zhTW.fields.keyValueValue)
  })

  // A bare component-absence check also passes for a hand-rolled <input>, so assert the vendored
  // composition's real data-slot hook directly: both cells of the row, and both buttons (remove
  // and add), not just the first one found.
  it('renders the vendored input and button data-slot hooks throughout the row', () => {
    const w = mount(KeyValueField, { props: { field: field({ interface: 'keyValue' }), modelValue: { a: '1' } }, ...opts })
    expect(w.findAll('[data-slot="input"]')).toHaveLength(2)
    expect(w.findAll('[data-slot="button"]')).toHaveLength(2)
  })

  // Mounted with a non-empty model so the row's inputs and remove button actually exist —
  // mounting an empty model would assert nothing about the row controls at all. A read-only or
  // RBAC-read-only field must not offer any editable or delete affordance.
  it('genuinely disables every control — both row inputs, the remove button, and the add button', () => {
    const w = mount(KeyValueField, {
      props: { field: field({ interface: 'keyValue' }), modelValue: { a: '1' }, disabled: true }, ...opts,
    })
    const inputs = w.findAll('.kv-row input')
    expect(inputs).toHaveLength(2)
    inputs.forEach(input => expect(input.attributes('disabled')).toBe(''))
    expect(w.get('.kv-remove').attributes('disabled')).toBe('')
    expect(w.get('.kv-add').attributes('disabled')).toBe('')
  })

  // A post-mount prop change is the only assertion that distinguishes a prop-driven control from
  // one seeded once and then left alone; ItemFormView's "Reload latest" and the revisions drawer's
  // revert both replace the whole model after mount, so both cells of the row must follow a model
  // swap, not just reflect it on the first render.
  it('reflects a post-mount model replacement on both cells of the row', async () => {
    const w = mount(KeyValueField, { props: { field: field({ interface: 'keyValue' }), modelValue: { a: '1' } }, ...opts })
    let inputs = w.findAll('.kv-row input')
    expect((inputs[0].element as HTMLInputElement).value).toBe('a')
    expect((inputs[1].element as HTMLInputElement).value).toBe('1')
    await w.setProps({ modelValue: { b: '2' } })
    inputs = w.findAll('.kv-row input')
    expect((inputs[0].element as HTMLInputElement).value).toBe('b')
    expect((inputs[1].element as HTMLInputElement).value).toBe('2')
  })

  // Native <button> defaults to type="submit". KeyValueField is dispatched inside ItemForm.vue's
  // <form @submit.prevent>, so an untyped button here would submit (and, for a Revisions-enabled
  // collection, snapshot) the whole record on every click instead of just editing this field.
  it('gives the remove and add buttons an explicit type="button"', () => {
    const w = mount(KeyValueField, { props: { field: field({ interface: 'keyValue' }), modelValue: { a: '1' } }, ...opts })
    expect(w.get('.kv-remove').attributes('type')).toBe('button')
    expect(w.get('.kv-add').attributes('type')).toBe('button')
  })
})
