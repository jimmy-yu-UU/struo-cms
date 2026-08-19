import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import TagsField from './TagsField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { common: { delete: 'Delete' }, fields: {
    add: 'Add', tagValue: 'value', tagDisplayText: 'display text (optional)',
  } } },
})
const opts = { global: { plugins: [i18n] } }

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

  it('drops the label key entirely when the display-text input is cleared', async () => {
    const w = mount(TagsField, {
      props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a', label: 'x' }] }, ...opts,
    })
    const inputs = w.findAll('.tag-row input')
    await inputs[1].setValue('')
    // toStrictEqual (not toEqual): toEqual ignores properties whose value is `undefined`, so it
    // cannot tell { value: 'a' } apart from { value: 'a', label: undefined } — and the contract
    // this test guards is specifically that the label key is absent, not merely undefined.
    expect(w.emitted('update:modelValue')?.at(-1)).toStrictEqual([[{ value: 'a' }]])
  })

  it('gives the icon-only remove control a resolved accessible name', () => {
    const w = mount(TagsField, { props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a' }] }, ...opts })
    // The remove control must stay reachable by an accessible name, not just by class: it is an
    // icon-only button, and an icon-only button with no label is invisible to assistive tech.
    // Asserting the exact resolved string (not just truthy) catches an unresolved i18n key, which
    // vue-i18n renders as the raw key string — truthy, but not a real name.
    expect(w.get('.tag-remove').attributes('aria-label')).toBe('Delete')
  })

  // A bare component-absence check also passes for a hand-rolled <input>, so assert the vendored
  // composition's real data-slot hook directly: both cells of a row, and both buttons (remove and
  // add), not just the first one found.
  it('renders the vendored input and button data-slot hooks throughout the row', () => {
    const w = mount(TagsField, { props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a' }] }, ...opts })
    expect(w.findAll('[data-slot="input"]')).toHaveLength(2)
    expect(w.findAll('[data-slot="button"]')).toHaveLength(2)
  })

  // Mounted with a non-empty model so the row's remove button actually exists — mounting an empty
  // model would assert nothing about the row controls at all. A read-only or RBAC-read-only field
  // must not offer any editable or delete affordance.
  it('genuinely disables every control — both row inputs, the remove button, and the add button', () => {
    const w = mount(TagsField, {
      props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a', label: 'x' }], disabled: true }, ...opts,
    })
    const inputs = w.findAll('.tag-row input')
    expect(inputs).toHaveLength(2)
    inputs.forEach(input => expect(input.attributes('disabled')).toBe(''))
    expect(w.get('.tag-remove').attributes('disabled')).toBe('')
    expect(w.get('.tag-add').attributes('disabled')).toBe('')
  })

  // A post-mount prop change is the only assertion that distinguishes a prop-driven control from
  // one seeded once from defaultValue and then left alone; ItemFormView's "Reload latest" and the
  // revisions drawer's revert both replace the whole model after mount, so both cells of every row
  // must follow a model swap, not just reflect it on the first render.
  it('reflects a post-mount model replacement on both cells of every row', async () => {
    const w = mount(TagsField, { props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a', label: 'alpha' }] }, ...opts })
    let inputs = w.findAll('.tag-row input')
    expect((inputs[0].element as HTMLInputElement).value).toBe('a')
    expect((inputs[1].element as HTMLInputElement).value).toBe('alpha')
    await w.setProps({ modelValue: [{ value: 'b', label: 'beta' }] })
    inputs = w.findAll('.tag-row input')
    expect((inputs[0].element as HTMLInputElement).value).toBe('b')
    expect((inputs[1].element as HTMLInputElement).value).toBe('beta')
  })

  // Without an aria-label, both inputs are unnamed native text boxes — a screen reader announces
  // "edit text" twice per row with no way to tell the value cell from the display-text cell.
  it('gives the value and display-text inputs distinct, resolved accessible names', () => {
    const w = mount(TagsField, { props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a' }] }, ...opts })
    const [valueInput, labelInput] = w.findAll('.tag-row input')
    expect(valueInput.attributes('aria-label')).toBe('value')
    expect(labelInput.attributes('aria-label')).toBe('display text (optional)')
    expect(valueInput.attributes('aria-label')).not.toBe(labelInput.attributes('aria-label'))
  })

  // Native <button> defaults to type="submit". TagsField is dispatched inside ItemForm.vue's
  // <form @submit.prevent>, so an untyped button here would submit (and, for a Revisions-enabled
  // collection, snapshot) the whole record on every tag click instead of just editing this array.
  it('gives the remove and add buttons an explicit type="button"', () => {
    const w = mount(TagsField, { props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a' }] }, ...opts })
    expect(w.get('.tag-remove').attributes('type')).toBe('button')
    expect(w.get('.tag-add').attributes('type')).toBe('button')
  })
})
