import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { createI18n } from 'vue-i18n'
import TagsField from './TagsField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { common: { delete: 'Delete' }, fields: { add: 'Add' } } },
})
const opts = { global: { plugins: [PrimeVue, i18n] } }

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
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([[{ value: 'a' }]])
  })

  it('no longer renders PrimeVue controls', () => {
    const w = mount(TagsField, { props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a' }] }, ...opts })
    expect(w.findComponent({ name: 'InputText' }).exists()).toBe(false)
    // The remove control must stay reachable by an accessible name, not just by class: it is an
    // icon-only button, and an icon-only button with no label is invisible to assistive tech.
    expect(w.get('.tag-remove').attributes('aria-label')).toBeTruthy()
  })

  // The migration's own assertion: the vendored composition's real data-slot hooks must be present
  // (standing-constraints's test-assertion pattern) on both the value and label inputs per row.
  it('renders the vendored input data-slot hook on both cells of a row', () => {
    const w = mount(TagsField, { props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a' }] }, ...opts })
    expect(w.findAll('[data-slot="input"]')).toHaveLength(2)
    expect(w.find('[data-slot="button"]').exists()).toBe(true)
  })

  // Mounted with a non-empty model so the row's remove button actually exists — an empty-model
  // mount (Task 9's mistake) would assert nothing about the row controls at all. A read-only or
  // RBAC-read-only field must not offer any editable or delete affordance.
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

  // Inbound direction (standing-constraints Task-5 rule), pinned past the initial render (Task-8's
  // fix round): a post-mount setProps is the only thing that distinguishes a genuinely prop-driven
  // control from one seeded once via defaultValue and then left alone (ItemFormView's "Reload
  // latest" and the revisions drawer's revert both replace the whole model after mount).
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
})
