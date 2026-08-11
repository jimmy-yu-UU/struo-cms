import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import TextField from './TextField.vue'
import TextareaField from './TextareaField.vue'
import NumberField from './NumberField.vue'
import BooleanField from './BooleanField.vue'
import DateField from './DateField.vue'
import SelectField from './SelectField.vue'
import RadioField from './RadioField.vue'
import DividerField from './DividerField.vue'
import ReadonlyField from './ReadonlyField.vue'
import type { FieldMeta } from '../../types/schema'
import { setActivePinia, createPinia } from 'pinia'
import { flushPromises } from '@vue/test-utils'
import { vi } from 'vitest'
import { createI18n } from 'vue-i18n'
import RichTextField from './RichTextField.vue'
import FileField from './FileField.vue'
import FilePicker from './FilePicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: {
    noFileSelected: 'No file selected', selectFile: 'Select', clear: 'Clear', selectAFile: 'Select a file',
    searchFiles: 'Search files…', loadFilesFailed: 'Failed to load files.',
  } } },
})

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }

describe('field components (simple inputs)', () => {
  it('TextField renders an input, binds maxlength, and emits on input', async () => {
    const w = mount(TextField, { props: { field: field({ interface: 'text', maxLength: 50 }), modelValue: '' }, ...opts })
    const input = w.get('input')
    expect(input.attributes('maxlength')).toBe('50')
    await input.setValue('hi')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['hi'])
  })

  it('TextField omits maxlength when field has none', () => {
    const w = mount(TextField, { props: { field: field({ interface: 'text', maxLength: null }), modelValue: '' }, ...opts })
    expect(w.get('input').attributes('maxlength')).toBeUndefined()
  })

  it('TextareaField renders a textarea and binds maxlength', () => {
    const w = mount(TextareaField, { props: { field: field({ interface: 'textarea', maxLength: 20 }), modelValue: '' }, ...opts })
    expect(w.get('textarea').attributes('maxlength')).toBe('20')
  })

  it('NumberField renders a numeric input', () => {
    const w = mount(NumberField, { props: { field: field({ interface: 'number' }), modelValue: 3 }, ...opts })
    expect(w.find('input').exists()).toBe(true)
  })

  // The migration's own assertion: this field must no longer resolve a PrimeVue component, and the
  // vendored composition's real data-slot hook must be present (see standing-constraints's
  // test-assertion pattern — a negative assertion alone also passes for a hand-rolled <input>).
  it('NumberField renders the vendored number-field input, not PrimeVue InputNumber', () => {
    const w = mount(NumberField, { props: { field: field({ interface: 'number' }), modelValue: 3 }, ...opts })
    expect(w.findComponent({ name: 'InputNumber' }).exists()).toBe(false)
    expect(w.find('[data-slot="input"]').exists()).toBe(true)
  })

  it('NumberField genuinely disables the vendored input control', () => {
    const w = mount(NumberField, { props: { field: field({ interface: 'number' }), modelValue: 3, disabled: true }, ...opts })
    expect(w.find('[data-slot="input"]').attributes('disabled')).toBe('')
  })

  // reka's NumberFieldRoot models a cleared field as `undefined` on `update:modelValue` (confirmed
  // by direct observation of the vendored component — see task-4-report.md's Step 1 findings), not
  // `null` and not a bare NaN. The CMS model wants null: a nullable numeric column legitimately
  // clears, and a NaN would survive into buildItemPayload and poison any arithmetic on the way.
  // NumberField.vue normalises undefined/NaN -> null at this boundary so nothing downstream has to
  // know reka's convention.
  it('NumberField normalises a cleared input to null', async () => {
    const w = mount(NumberField, { props: { field: field({ interface: 'number' }), modelValue: 3 }, ...opts })
    await w.get('input').setValue('')
    await w.get('input').trigger('blur')
    const emitted = w.emitted('update:modelValue')
    expect(emitted).toBeTruthy()
    expect(emitted![emitted!.length - 1][0]).toBeNull()
  })

  it('BooleanField renders a checkbox and is disabled when asked', () => {
    const w = mount(BooleanField, { props: { field: field({ interface: 'boolean' }), modelValue: false, disabled: true }, ...opts })
    const box = w.get('[role="checkbox"]')
    // Step 1 established this concretely: reka's CheckboxRoot renders a real <button>, so
    // `disabled` reaches it as the native HTML attribute (not aria-disabled).
    expect(box.attributes('disabled')).toBe('')
  })

  // Constraint 9: reka components bind their own onClick, so the emit path is only proven by
  // actually clicking. A rendering-only assertion here would have passed even when broken.
  it('BooleanField emits true when clicked from false', async () => {
    const w = mount(BooleanField, { props: { field: field({ interface: 'boolean' }), modelValue: false }, ...opts })
    await w.get('[role="checkbox"]').trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual([true])
  })

  // Fix round 1: the three tests above all mounted with modelValue: false, so nothing pinned the
  // inbound direction of BooleanField's `:model-value="modelValue === true"` binding. A deleted or
  // inverted binding would have stayed green. aria-checked is reka's unconditional, real-ARIA hook
  // (CheckboxRoot.js) and is what Playwright reads.
  it('BooleanField reflects a true model as checked', () => {
    const w = mount(BooleanField, { props: { field: field({ interface: 'boolean' }), modelValue: true }, ...opts })
    expect(w.get('[role="checkbox"]').attributes('aria-checked')).toBe('true')
  })

  it('BooleanField emits false when clicked from true', async () => {
    const w = mount(BooleanField, { props: { field: field({ interface: 'boolean' }), modelValue: true }, ...opts })
    await w.get('[role="checkbox"]').trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual([false])
  })

  // The migration's own assertion: the vendored composition's real data-slot hook must be present
  // (see standing-constraints's test-assertion pattern).
  it('BooleanField renders the vendored checkbox data-slot hook', () => {
    const w = mount(BooleanField, { props: { field: field({ interface: 'boolean' }), modelValue: false }, ...opts })
    expect(w.find('[data-slot="checkbox"]').exists()).toBe(true)
  })

  it('DateField sets time-only for time and show-time for dateTime', () => {
    const t = mount(DateField, { props: { field: field({ interface: 'time' }), modelValue: null }, ...opts })
    expect(t.findComponent({ name: 'DatePicker' }).props('timeOnly')).toBe(true)
    const dt = mount(DateField, { props: { field: field({ interface: 'dateTime' }), modelValue: null }, ...opts })
    expect(dt.findComponent({ name: 'DatePicker' }).props('showTime')).toBe(true)
  })

  // The migration's own assertion: this field must no longer resolve a PrimeVue component.
  // findComponent({ name }) is the same lookup the pre-migration tests used for Select/DatePicker.
  it('TextField renders the vendored Input, not PrimeVue InputText', () => {
    const w = mount(TextField, { props: { field: field({ interface: 'text' }), modelValue: 'x' }, ...opts })
    expect(w.findComponent({ name: 'InputText' }).exists()).toBe(false)
    expect(w.find('input').exists()).toBe(true)
  })

  it('TextareaField keeps six rows after the migration', () => {
    const w = mount(TextareaField, { props: { field: field({ interface: 'textarea' }), modelValue: 'x' }, ...opts })
    expect(w.get('textarea').attributes('rows')).toBe('6')
  })
})

describe('field components (choice + structural)', () => {
  // The migration's own assertion: the vendored Select composition has no options prop (options
  // are SelectItem children), so the only thing worth pinning is what the trigger actually shows.
  // This is the inbound-direction assertion (standing-constraints's Task-5 rule): it proves the
  // control reflects a non-default incoming model, not just that something renders.
  it('SelectField shows the current option label on the trigger', async () => {
    const w = mount(SelectField, {
      props: {
        field: field({ interface: 'select', options: [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }] }),
        modelValue: 'b',
      },
      global: { plugins: [PrimeVue], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    // SelectContent teleports its (closed) items into an off-DOM DocumentFragment so SelectValue
    // can resolve the selected item's label from the collection even while unopened; that
    // registration lands one tick after the initial synchronous mount, so this needs a real
    // await before the trigger reflects it — see task-6-report.md for why the brief's un-awaited
    // version of this assertion does not hold.
    await w.vm.$nextTick()
    // The trigger is all that renders before the listbox opens; SelectValue reflects the selected
    // item's text. Asserting text, not classes (constraint 5).
    expect(w.get('[role="combobox"]').text()).toContain('Beta')
  })

  it('SelectField renders the vendored select-trigger data-slot hook', () => {
    const w = mount(SelectField, {
      props: { field: field({ interface: 'select', options: [{ value: 'a', label: 'Alpha' }] }), modelValue: 'a' },
      global: { plugins: [PrimeVue], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    expect(w.find('[data-slot="select-trigger"]').exists()).toBe(true)
  })

  it('SelectField genuinely disables the vendored trigger', () => {
    const w = mount(SelectField, {
      props: { field: field({ interface: 'select', options: [{ value: 'a', label: 'Alpha' }] }), modelValue: 'a', disabled: true },
      global: { plugins: [PrimeVue], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    expect(w.get('[role="combobox"]').attributes('disabled')).toBe('')
  })

  // jsdom cannot drive reka's Select through real pointer events, but VTU can emit directly from
  // the vendored child component, which runs SelectField's own template listener (and whatever
  // coercion it applies) rather than bypassing it. modelValue: '' mounts with select's real
  // registry empty value (registry.ts's def() default `empty`, which the `select` interface entry
  // does not override), keeping reka's SelectRoot in the controlled mode production always runs.
  it('SelectField emits the chosen option value through its real listener', async () => {
    const w = mount(SelectField, {
      props: { field: field({ interface: 'select', options: [{ value: 'a', label: 'Alpha' }] }), modelValue: '' },
      global: { plugins: [PrimeVue], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    await w.findComponent({ name: 'Select' }).vm.$emit('update:modelValue', 'a')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['a'])
  })

  // A vendored root emitting `undefined` (e.g. a hypothetical future clear action, or a different
  // root entirely) must not turn into the literal four-character string "undefined" in the saved
  // payload — the outbound coercion collapses a nullish emission to '' by construction.
  it('SelectField coerces a cleared emission to an empty string, never the string "undefined"', async () => {
    const w = mount(SelectField, {
      props: { field: field({ interface: 'select', options: [{ value: 'a', label: 'Alpha' }] }), modelValue: 'a' },
      global: { plugins: [PrimeVue], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    await w.findComponent({ name: 'Select' }).vm.$emit('update:modelValue', undefined)
    expect(w.emitted('update:modelValue')?.[0]).toEqual([''])
  })

  // Standing-constraints "Accessible name on every floating control": reka's SelectTrigger has no
  // id/aria-label/aria-labelledby of its own, so ItemForm.vue's <label :for="f.name"> dangles and
  // a screen reader announces only the picked value once one exists.
  it('SelectField gives its trigger an accessible name from the field label', () => {
    const w = mount(SelectField, {
      props: { field: field({ interface: 'select', label: 'Status', options: [{ value: 'a', label: 'Alpha' }] }), modelValue: '' },
      global: { plugins: [PrimeVue], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    expect(w.get('[role="combobox"]').attributes('aria-label')).toBe('Status')
  })

  it('RadioField renders one radio per choice inside a single radiogroup', () => {
    const w = mount(RadioField, {
      props: {
        field: field({ interface: 'radio', options: [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }] }),
        modelValue: 'a',
      },
      ...opts,
    })
    expect(w.findAll('[role="radio"]')).toHaveLength(2)
    expect(w.find('[role="radiogroup"]').exists()).toBe(true)
  })

  // Constraint 8: reka binds its own onClick, which runs before parent fallthrough, so this must
  // really click rather than synthesise an emit.
  it('RadioField emits the clicked option value', async () => {
    const w = mount(RadioField, {
      props: {
        field: field({ interface: 'radio', options: [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }] }),
        modelValue: 'a',
      },
      ...opts,
    })
    await w.findAll('[role="radio"]')[1].trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['b'])
  })

  // Pins the template's own @update:model-value listener on the vendored root directly: a deleted
  // listener would still pass the real-click test above only by accident of reka's internal wiring,
  // so this asserts the wrapper's own binding independently (same rationale as Task 6's fix round).
  it('RadioField relays the vendored RadioGroup root emit through its own listener', async () => {
    const w = mount(RadioField, {
      props: {
        field: field({ interface: 'radio', options: [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }] }),
        modelValue: 'a',
      },
      ...opts,
    })
    await w.findComponent({ name: 'RadioGroup' }).vm.$emit('update:modelValue', 'b')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['b'])
  })

  // Inbound direction (standing-constraints Task-5 rule): mount with a non-default model and assert
  // the matching option reflects checked state via reka's real data-state hook, never a class.
  it('RadioField reflects a non-default model on the matching radio', () => {
    const w = mount(RadioField, {
      props: {
        field: field({ interface: 'radio', options: [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }] }),
        modelValue: 'b',
      },
      ...opts,
    })
    const radios = w.findAll('[role="radio"]')
    expect(radios[0].attributes('data-state')).toBe('unchecked')
    expect(radios[1].attributes('data-state')).toBe('checked')
  })

  // reka's radio is a <button role="radio">, so wrapping it in a <label> gives no implicit
  // association; the explicit for/id pairing is what actually associates each label.
  it('RadioField associates each label with its radio via explicit for/id', () => {
    const w = mount(RadioField, {
      props: {
        field: field({ interface: 'radio', options: [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }] }),
        modelValue: 'a',
      },
      ...opts,
    })
    const labels = w.findAll('label')
    const radios = w.findAll('[role="radio"]')
    expect(labels).toHaveLength(2)
    labels.forEach((label, i) => {
      expect(label.attributes('for')).toBe(radios[i].attributes('id'))
    })
  })

  it('DividerField renders an hr', () => {
    const w = mount(DividerField, { props: { field: field({ interface: 'divider' }), modelValue: '' } })
    expect(w.find('hr').exists()).toBe(true)
  })

  it('ReadonlyField shows the value, em-dash when empty', () => {
    expect(mount(ReadonlyField, { props: { field: field({ interface: 'json' }), modelValue: '{}' } }).text()).toBe('{}')
    expect(mount(ReadonlyField, { props: { field: field({ interface: 'json' }), modelValue: null } }).find('.readonly-field').text()).toBe('—')
  })
})

describe('field components (rich-text + file wrappers)', () => {
  it('RichTextField relays the RichTextInput value', () => {
    const w = mount(RichTextField, {
      props: { field: field({ interface: 'richText' }), modelValue: '<p>hi</p>' },
      global: { stubs: { RichTextInput: { name: 'RichTextInput', props: ['modelValue'], template: '<div class="stub-rt" />' } } },
    })
    const rt = w.findComponent({ name: 'RichTextInput' })
    expect(rt.props('modelValue')).toBe('<p>hi</p>')
    rt.vm.$emit('update:modelValue', '<p>bye</p>')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['<p>bye</p>'])
  })

  it('RichTextField coerces a null model to empty string', () => {
    const w = mount(RichTextField, {
      props: { field: field({ interface: 'richText' }), modelValue: null },
      global: { stubs: { RichTextInput: { name: 'RichTextInput', props: ['modelValue'], template: '<div class="stub-rt" />' } } },
    })
    expect(w.findComponent({ name: 'RichTextInput' }).props('modelValue')).toBe('')
  })

  it('FileField sets image=true for the image interface and relays the id', async () => {
    setActivePinia(createPinia())
    useLanguageStore().languages = [{ code: 'en', name: 'English', isDefault: true }]
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 })
    const w = mount(FileField, {
      props: { field: field({ name: 'heroImageId', interface: 'image' }), modelValue: null },
      global: { plugins: [i18n], stubs: { Dialog: true, Button: true, MediaGrid: true } },
    })
    await flushPromises()
    const picker = w.findComponent(FilePicker)
    expect(picker.props('image')).toBe(true)
    picker.vm.$emit('update:modelValue', 'f1')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['f1'])
  })
})
