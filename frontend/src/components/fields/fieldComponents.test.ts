import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
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
import en from '../../locales/en'

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }
// form/DatePicker.vue calls useI18n() for its placeholder and trigger label, and its
// PopoverContent is one of reka's floating components whose own portal is itself named Teleport,
// which collides with VTU's stub unless slot rendering is switched back on.
const dateOpts = { global: { plugins: [PrimeVue, i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } }

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
  // vendored composition's real data-slot hook must be present — a negative assertion alone also
  // passes for a hand-rolled <input>, so a positive one is required too.
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
  // by direct observation of the vendored component), not `null` and not a bare NaN. The CMS
  // model wants null: a nullable numeric column legitimately
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

  // The migration's own assertion: the vendored composition's real data-slot hook must be present,
  // not just a negative assertion that the old PrimeVue component is gone.
  it('BooleanField renders the vendored checkbox data-slot hook', () => {
    const w = mount(BooleanField, { props: { field: field({ interface: 'boolean' }), modelValue: false }, ...opts })
    expect(w.find('[data-slot="checkbox"]').exists()).toBe(true)
  })

  // The three interfaces this one component serves render three different control sets, because
  // ui/calendar has no time part: date -> calendar only, time -> a time input only, dateTime ->
  // both.
  it('DateField renders a calendar for date, a time input for time, and both for dateTime', () => {
    const d = mount(DateField, { props: { field: field({ interface: 'date' }), modelValue: null }, ...dateOpts })
    expect(d.findComponent({ name: 'DatePicker' }).exists()).toBe(true)
    expect(d.find('input[type="time"]').exists()).toBe(false)

    const tm = mount(DateField, { props: { field: field({ interface: 'time' }), modelValue: null }, ...dateOpts })
    expect(tm.findComponent({ name: 'DatePicker' }).exists()).toBe(false)
    expect(tm.find('input[type="time"]').exists()).toBe(true)

    const dt = mount(DateField, { props: { field: field({ interface: 'dateTime' }), modelValue: null }, ...dateOpts })
    expect(dt.findComponent({ name: 'DatePicker' }).exists()).toBe(true)
    expect(dt.find('input[type="time"]').exists()).toBe(true)
  })

  // Positive assertion alongside the render check above: the time control really is the vendored
  // composition (data-slot="input" is ui/input's own hook), not a hand-rolled <input> that happens
  // to have type="time".
  it('DateField renders the vendored input for the time control', () => {
    const w = mount(DateField, { props: { field: field({ interface: 'time' }), modelValue: null }, ...dateOpts })
    expect(w.find('input[type="time"][data-slot="input"]').exists()).toBe(true)
  })

  // The fixture date deliberately cannot be "today": onTime's fallback path (`current.value ??
  // new Date()`) exists for the no-incoming-value case, and a fixture that happened to equal the
  // real clock's current date would pass even if that fallback fired instead of `current.value`.
  it('DateField merges a typed time into the existing date without losing the date', async () => {
    const w = mount(DateField, {
      props: { field: field({ interface: 'dateTime' }), modelValue: new Date(2020, 5, 15, 0, 0, 0) },
      ...dateOpts,
    })
    await w.get('input[type="time"]').setValue('14:30')
    const emitted = w.emitted('update:modelValue')?.at(-1)?.[0] as Date
    expect(emitted.getFullYear()).toBe(2020)
    expect(emitted.getMonth()).toBe(5)
    expect(emitted.getDate()).toBe(15)
    expect(emitted.getHours()).toBe(14)
    expect(emitted.getMinutes()).toBe(30)
  })

  // registry.ts's def() default `empty` is '' for date/time/dateTime, but a value already loaded
  // from the API's raw JSON is a string that failed to parse (a malformed or truncated value) just
  // as plausibly as it is a well-formed one; this must degrade to no value, not throw. Asserted
  // through DatePicker's own modelValue prop rather than the time input's DOM value: a native
  // <input type="time"> silently sanitises an invalid string back to '' on its own, which would
  // make this assertion pass even if `current`'s Number.isNaN guard were removed entirely.
  it('DateField treats an unparseable string model as no value rather than throwing', () => {
    const w = mount(DateField, {
      props: { field: field({ interface: 'dateTime' }), modelValue: 'not-a-real-date' },
      ...dateOpts,
    })
    expect(w.findComponent({ name: 'DatePicker' }).props('modelValue')).toBeNull()
  })

  // The reverse direction: DatePicker carries the incoming time-of-day forward on its own, but
  // this proves that end-to-end through DateField's own wiring — a dropped `:model-value="current"`
  // binding on DatePicker would still pass the merge test above while this one caught it.
  it('DateField preserves an existing time when a new date is picked through the real calendar', async () => {
    const w = mount(DateField, {
      props: { field: field({ interface: 'dateTime' }), modelValue: new Date(2026, 7, 11, 14, 30, 0, 0) },
      ...dateOpts,
    })
    await w.get('button').trigger('click')
    await w.vm.$nextTick()
    await w.vm.$nextTick()
    await w.get('[data-value="2026-08-20"]').trigger('click')
    const emitted = w.emitted('update:modelValue')?.at(-1)?.[0] as Date
    expect(emitted.getDate()).toBe(20)
    expect(emitted.getHours()).toBe(14)
    expect(emitted.getMinutes()).toBe(30)
  })

  it('DateField clears the value entirely when the time is cleared on a time-only field', async () => {
    const w = mount(DateField, {
      props: { field: field({ interface: 'time' }), modelValue: new Date(2026, 7, 11, 14, 30, 0, 0) },
      ...dateOpts,
    })
    await w.get('input[type="time"]').setValue('')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([null])
  })

  // A dateTime field still has a date the user chose; dropping it because the time box went blank
  // would be a surprising side effect of an unrelated control, so it drops to midnight instead.
  it('DateField drops to midnight, not null, when the time is cleared on a dateTime field', async () => {
    const w = mount(DateField, {
      props: { field: field({ interface: 'dateTime' }), modelValue: new Date(2026, 7, 11, 14, 30, 0, 0) },
      ...dateOpts,
    })
    await w.get('input[type="time"]').setValue('')
    const emitted = w.emitted('update:modelValue')?.at(-1)?.[0] as Date | null
    expect(emitted).not.toBeNull()
    expect(emitted!.getFullYear()).toBe(2026)
    expect(emitted!.getMonth()).toBe(7)
    expect(emitted!.getDate()).toBe(11)
    expect(emitted!.getHours()).toBe(0)
    expect(emitted!.getMinutes()).toBe(0)
  })

  // The model arrives as a Date once ItemForm round-trips a save, or as an ISO string on the very
  // first load of an existing item (the API's raw JSON). A string without a timezone suffix
  // parses as local time, sidestepping any dependence on the test runner's timezone.
  it('DateField accepts an ISO string model the same as a Date', () => {
    const w = mount(DateField, {
      props: { field: field({ interface: 'time' }), modelValue: '2026-08-11T14:30:00' },
      ...dateOpts,
    })
    expect((w.get('input[type="time"]').element as HTMLInputElement).value).toBe('14:30')
  })

  // registry.ts's def() default `empty` is '' for date/time/dateTime (none of the three entries
  // override it), so this is the value ItemForm.vue actually supplies on a fresh item, not null.
  it('DateField renders empty controls when mounted with the registry\'s real empty value', () => {
    const w = mount(DateField, { props: { field: field({ interface: 'dateTime' }), modelValue: '' }, ...dateOpts })
    expect(w.findComponent({ name: 'DatePicker' }).props('modelValue')).toBeNull()
    expect((w.get('input[type="time"]').element as HTMLInputElement).value).toBe('')
  })

  it('DateField genuinely disables both the calendar trigger and the time input', () => {
    const w = mount(DateField, {
      props: { field: field({ interface: 'dateTime' }), modelValue: null, disabled: true },
      ...dateOpts,
    })
    expect(w.findComponent({ name: 'DatePicker' }).props('disabled')).toBe(true)
    // The prop boundary alone would still pass if DatePicker stopped forwarding it to its own
    // trigger; assert the rendered button too, the way the other migrated fields assert their own
    // native control rather than stopping at the prop.
    expect(w.get('button').attributes('disabled')).toBeDefined()
    expect(w.get('input[type="time"]').attributes('disabled')).toBe('')
  })

  // An inbound assertion made only at initial render cannot distinguish controlled binding from
  // local state seeded once and never revisited; the prop must change after mount and the
  // controls must follow.
  it('DateField reflects a model value changed after mount', async () => {
    const w = mount(DateField, { props: { field: field({ interface: 'dateTime' }), modelValue: null }, ...dateOpts })
    expect((w.get('input[type="time"]').element as HTMLInputElement).value).toBe('')
    await w.setProps({ modelValue: new Date(2026, 7, 11, 9, 15, 0, 0) })
    expect((w.get('input[type="time"]').element as HTMLInputElement).value).toBe('09:15')
    expect(w.findComponent({ name: 'DatePicker' }).props('modelValue')).toEqual(new Date(2026, 7, 11, 9, 15, 0, 0))
  })

  // Two controls in one dateTime field must not share an accessible name, or a screen reader
  // announces both as the same control once a value exists. Asserted against the resolved locale
  // message (not a literal string) so a change to fields.timePart's wording cannot silently
  // desync the test from what the component actually renders.
  it('DateField gives the time input an accessible name distinct from the calendar trigger', () => {
    const w = mount(DateField, {
      props: { field: field({ interface: 'dateTime', label: 'Published at' }), modelValue: null },
      ...dateOpts,
    })
    expect(w.findComponent({ name: 'DatePicker' }).props('label')).toBe('Published at')
    expect(w.get('input[type="time"]').attributes('aria-label')).toBe(`Published at ${en.fields.timePart}`)
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
  // This is the inbound-direction assertion: mounting with a non-default incoming model and
  // asserting the control reflects it, which proves the binding is real rather than just that
  // something renders.
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
    // await before the trigger reflects it; an un-awaited assertion here would pass only because
    // it runs before the trigger has caught up, not because the binding is correct.
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

  // Inbound direction: mount with a non-default model and assert the matching option reflects
  // checked state via reka's real data-state hook, never a class.
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
  // association; the explicit for/id pairing is what actually associates each label. Asserting
  // non-empty AND equal (not just equal) so the check cannot pass with both sides blanked out.
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
      const forAttr = label.attributes('for')
      expect(forAttr).toBeTruthy()
      expect(forAttr).toBe(radios[i].attributes('id'))
    })
  })

  // Repeaters (RepeaterField.vue) render this component once per row, as siblings inside one Vue
  // app tree — not as separate mounts — with the same field.name. An id scheme keyed on
  // field.name/option.value alone would collide across rows and resolve every row's <label for>
  // and reka's own [for=…] lookup to the first row's element. useId()'s counter is scoped to a
  // single app instance (two independent mount() calls each start their own app and would both
  // start counting from zero, which would defeat this exact assertion), so the host component
  // below renders two RadioField as siblings in one tree to match the real repeater shape.
  it('RadioField scopes ids per component instance, not just per option', () => {
    const f = field({ interface: 'radio', options: [{ value: 'a', label: 'Alpha' }] })
    const Host = defineComponent({
      render: () => h('div', [h(RadioField, { field: f, modelValue: 'a' }), h(RadioField, { field: f, modelValue: 'a' })]),
    })
    const w = mount(Host, opts)
    const radios = w.findAll('[role="radio"]')
    expect(radios).toHaveLength(2)
    expect(radios[0].attributes('id')).toBeTruthy()
    expect(radios[0].attributes('id')).not.toBe(radios[1].attributes('id'))
  })

  // Standing-constraints "Accessible name on every floating control" extends to the radiogroup:
  // reka's role="radiogroup" element carries no aria-label/aria-labelledby of its own, so
  // ItemForm.vue's <label :for="f.name"> dangles (no element in the DOM carries id="f.name").
  // aria-label isn't a declared RadioGroupRootProps key, so this pins the attrs-fallthrough path
  // through the vendored wrapper rather than assuming it survives a future re-vendor.
  it('RadioField gives its radiogroup an accessible name from the field label', () => {
    const w = mount(RadioField, {
      props: {
        field: field({ interface: 'radio', label: 'Status', options: [{ value: 'a', label: 'Alpha' }] }),
        modelValue: 'a',
      },
      ...opts,
    })
    expect(w.get('[role="radiogroup"]').attributes('aria-label')).toBe('Status')
  })

  // disabled is not forwarded directly to reka's Radio; it is re-derived via provide/inject
  // (rootContext.disabled.value || props.disabled) across RadioGroupRoot -> RadioGroupItem ->
  // Radio. A dropped `:disabled="disabled"` on <RadioGroup> would leave every other RadioField
  // test green while a read-only/RBAC-read-only field rendered fully clickable.
  it('RadioField genuinely disables every radio when asked', () => {
    const w = mount(RadioField, {
      props: {
        field: field({ interface: 'radio', options: [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }] }),
        modelValue: 'a',
        disabled: true,
      },
      ...opts,
    })
    const radios = w.findAll('[role="radio"]')
    expect(radios[0].attributes('disabled')).toBe('')
    expect(radios[1].attributes('disabled')).toBe('')
  })

  // Inbound direction, registry's real empty value: registry.ts's def() default `empty` is '' for
  // the `radio` interface (it does not override it), so this is the actual value ItemForm.vue
  // supplies on a fresh item, not null/undefined. Reading the vendored RadioGroup's own
  // model-value prop (rather than an ARIA hook) pins that the coercion keeps the control in
  // controlled mode — `current`'s prior `== null ? undefined : ...` would have passed `undefined`
  // for a null model and flipped reka into uncontrolled (passive) mode, which production never
  // exercises.
  it('RadioField stays controlled when mounted with the registry\'s real empty value', () => {
    const w = mount(RadioField, {
      props: {
        field: field({ interface: 'radio', options: [{ value: 'a', label: 'Alpha' }] }),
        modelValue: '',
      },
      ...opts,
    })
    expect(w.findComponent({ name: 'RadioGroup' }).props('modelValue')).toBe('')
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
