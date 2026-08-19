import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { createI18n } from 'vue-i18n'
import MultiSelectField from './MultiSelectField.vue'
import CheckboxGroupField from './CheckboxGroupField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: {
    noOptions: 'No options', selectedCount: '{n} selected', removeOption: 'Remove {label}', searchOptions: 'Search options…',
    namePairSeparator: ': ', nameListSeparator: ', ',
  } } },
})
// A separate instance locked to zh-TW, mirroring the real pack's separators — the only way to prove
// the joiner comes from i18n rather than from a hardcoded template literal that happens to read back
// identically for English.
const i18nZh = createI18n({
  legacy: false, locale: 'zh-TW', fallbackLocale: 'zh-TW',
  messages: { 'zh-TW': { fields: {
    noOptions: '沒有選項', selectedCount: '已選 {n} 項', removeOption: '移除 {label}', searchOptions: '搜尋選項…',
    namePairSeparator: '：', nameListSeparator: '，',
  } } },
})

describe('MultiSelectField', () => {
  const options = [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }]
  const comboOpts = { global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } }
  const comboOptsZh = { global: { plugins: [i18nZh], stubs: { teleport: true }, renderStubDefaultSlot: true } }

  it('renders a chip per selected value', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: ['a'] }, ...comboOpts })
    expect(w.text()).toContain('Alpha')
    expect(w.text()).not.toContain('Beta')
  })

  it('renders the vendored combobox trigger', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: [] }, ...comboOpts })
    expect(w.find('[data-slot="combobox-trigger"]').exists()).toBe(true)
  })

  // reka's ComboboxItem/ListboxItem select on a real `click` (unlike ui/select's SelectItem, which
  // is pointerdown/pointerup), so once the popup is open jsdom can drive the actual production
  // interaction directly on the rendered option — no exposed setter, no synthesised $emit.
  it('appends a clicked option immutably', async () => {
    const before = ['a']
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: before }, ...comboOpts })
    await w.get('[data-slot="combobox-trigger"]').trigger('click')
    await w.findAll('[role="option"]')[1].trigger('click') // Beta
    expect(w.emitted('update:modelValue')?.[0]).toEqual([['a', 'b']])
    expect(before).toEqual(['a'])
  })

  it('removes an already-selected option via the same click path', async () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: ['a', 'b'] }, ...comboOpts })
    await w.get('[data-slot="combobox-trigger"]').trigger('click')
    await w.findAll('[role="option"]')[0].trigger('click') // Alpha
    expect(w.emitted('update:modelValue')?.[0]).toEqual([['b']])
  })

  it('treats a null model as an empty selection', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: null }, ...comboOpts })
    expect(w.emitted('update:modelValue')).toBeUndefined()
    expect(w.text()).not.toContain('Alpha')
  })

  it('gives the trigger an accessible name from the field label', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', label: 'Regions', options }), modelValue: [] }, ...comboOpts })
    expect(w.get('[data-slot="combobox-trigger"]').attributes('aria-label')).toBe('Regions')
  })

  // The visible summary and the announced name are two different things: `aria-label` overrides
  // name-from-contents entirely, so without folding the count in, a screen-reader user would hear
  // only "Regions" regardless of how many values are selected.
  it('folds the selection count into the trigger\'s accessible name once something is selected', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', label: 'Regions', options }), modelValue: ['a'] }, ...comboOpts })
    expect(w.get('[data-slot="combobox-trigger"]').attributes('aria-label')).toBe('Regions, 1 selected')
  })

  it('uses the CJK separator in zh-TW', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', label: '地區', options }), modelValue: ['a'] }, ...comboOptsZh })
    expect(w.get('[data-slot="combobox-trigger"]').attributes('aria-label')).toBe('地區，已選 1 項')
  })

  it('shows a placeholder when empty and a count once something is selected', () => {
    const empty = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', label: 'Regions', options }), modelValue: [] }, ...comboOpts })
    expect(empty.get('[data-slot="combobox-trigger"]').text()).toContain('Regions')

    const withSelection = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', label: 'Regions', options }), modelValue: ['a', 'b'] }, ...comboOpts })
    expect(withSelection.get('[data-slot="combobox-trigger"]').text()).toContain('2 selected')
  })

  it('genuinely disables the trigger', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: [], disabled: true }, ...comboOpts })
    expect(w.get('[data-slot="combobox-trigger"]').attributes('disabled')).toBe('')
  })

  // Chips render from this component's own `selected` computed reading `props.modelValue`
  // directly rather than from any reka-owned state, so an initial-render-only assertion would stay
  // green even if the prop stopped being read on updates. Asserting again after a real `setProps`
  // is what actually proves the binding stays live, not just correct at mount.
  it('reflects a non-default model on the chip row, including after the model changes', async () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: ['a'] }, ...comboOpts })
    expect(w.text()).toContain('Alpha')
    expect(w.text()).not.toContain('Beta')
    await w.setProps({ modelValue: ['b'] })
    expect(w.text()).not.toContain('Alpha')
    expect(w.text()).toContain('Beta')
  })

  // Every prior assertion reads our own markup (chips, emit, data-slot, aria-label, disabled) —
  // none of them would notice `:model-value="selected"` being deleted from `<Combobox>`, since the
  // chips and the click-driven emits never touch reka's own state. `ListboxItem` sets
  // `aria-selected` from the root's own model (`valueComparator` handles arrays via `.some`), so
  // asserting it — with the popup genuinely open — is the only way to pin that binding. Checked
  // again after `setProps` because reka reads `passive` once at setup: an initial-render-only
  // assertion cannot distinguish a live prop from a value that was only ever right at mount.
  it('reflects the incoming model as aria-selected on the options, including after the model changes', async () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: ['a'] }, ...comboOpts })
    await w.get('[data-slot="combobox-trigger"]').trigger('click')
    const items = w.findAll('[role="option"]')
    expect(items[0].attributes('aria-selected')).toBe('true')
    expect(items[1].attributes('aria-selected')).toBe('false')

    await w.setProps({ modelValue: ['b'] })
    expect(items[0].attributes('aria-selected')).toBe('false')
    expect(items[1].attributes('aria-selected')).toBe('true')
  })

  // A remove control nested inside the trigger <button> would be invalid HTML and a real
  // click/focus hazard, so it lives in its own chip row instead, driven by the same immutable
  // toggleValue as the in-list click path — no popup needs to be open to remove a value.
  it('removes a value by clicking its own chip remove button, without opening the popup', async () => {
    const before = ['a', 'b']
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: before }, ...comboOpts })
    const removeAlpha = w.get('[aria-label="Remove Alpha"]')
    expect(removeAlpha.element.tagName).toBe('BUTTON')
    await removeAlpha.trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual([['b']])
    expect(before).toEqual(['a', 'b'])
  })

  // The test name alone doesn't prove the structure: this asserts the remove button is a sibling
  // of the trigger, not a descendant of it, so a future regression back to nesting it inside the
  // trigger <button> (the mistake this layout exists to avoid) fails here.
  it('keeps the chip remove button outside the trigger, not nested inside it', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: ['a'] }, ...comboOpts })
    const trigger = w.get('[data-slot="combobox-trigger"]')
    const removeButton = w.get('[aria-label="Remove Alpha"]')
    expect(trigger.element.contains(removeButton.element)).toBe(false)
  })

  it('keeps each chip remove button independently keyboard-reachable', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: ['a', 'b'] }, ...comboOpts })
    const removeButtons = w.findAll('button[aria-label^="Remove "]')
    expect(removeButtons).toHaveLength(2)
    for (const btn of removeButtons) expect(btn.attributes('tabindex')).not.toBe('-1')
  })

  // The chip remove button is a second write surface with no reka gating of its own (unlike the
  // trigger, which reka itself disables via the shared Listbox context) — this one line is the
  // only thing standing between a read-only/RBAC-read-only field and a user deleting stored values.
  it('genuinely disables each chip remove button too', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: ['a'], disabled: true }, ...comboOpts })
    const removeButtons = w.findAll('button[aria-label^="Remove "]')
    expect(removeButtons.length).toBeGreaterThan(0)
    for (const btn of removeButtons) expect(btn.attributes('disabled')).toBe('')
  })

  // Native <button> defaults to type="submit". This chip button is a hand-rolled <button>, not the
  // vendored reka trigger primitives that inject their own type — so it needs the attribute stated
  // directly. It sits inside ItemForm.vue's <form @submit.prevent>, so an untyped button here would
  // submit the whole record on a click that should only remove one chip.
  it('gives each chip remove button an explicit type="button"', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: ['a', 'b'] }, ...comboOpts })
    const removeButtons = w.findAll('button[aria-label^="Remove "]')
    expect(removeButtons.length).toBeGreaterThan(0)
    for (const btn of removeButtons) expect(btn.attributes('type')).toBe('button')
  })
})

describe('CheckboxGroupField', () => {
  const twoOptions = [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }]

  it('renders one checkbox per option', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: [] } })
    expect(w.findAll('[role="checkbox"]')).toHaveLength(2)
  })

  it('gives the group an accessible name since the label-for-field.name pairing in ItemForm.vue does not resolve to any of these checkboxes', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions, label: 'Regions' }), modelValue: [] } })
    expect(w.find('[role="group"][aria-label="Regions"]').exists()).toBe(true)
  })

  // The whole behaviour of this field is array arithmetic, so it must be driven by real clicks:
  // asserting a synthesised $emit would test the test, not the component.
  it('appends the clicked option immutably', async () => {
    const before = ['a']
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: before } })
    await w.findAll('[role="checkbox"]')[1].trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual([['a', 'b']])
    expect(before).toEqual(['a'])
  })

  it('removes an already-checked option immutably', async () => {
    const before = ['a', 'b']
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: before } })
    await w.findAll('[role="checkbox"]')[0].trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual([['b']])
    expect(before).toEqual(['a', 'b'])
  })

  it('treats a null model as an empty selection', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: null } })
    expect(w.findAll('[role="checkbox"][aria-checked="true"]')).toHaveLength(0)
  })

  // Inbound direction: mount with a non-default model and assert the matching checkbox, not just
  // the emit, reflects it. A prop change after mount is asserted too, not only the initial
  // render: reka's Checkbox computes
  // `passive: props.modelValue === void 0` once at setup, so an `undefined`-fed `:model-value` paired
  // with a same-valued `:default-value` would render identically on the very first paint and only
  // diverge once the model changes again without a remount — exactly what ItemFormView.vue's
  // 409-recovery reload and the revisions-drawer revert both do.
  it('reflects a non-default model on the matching checkbox, including after the model changes', async () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: ['b'] } })
    const boxes = w.findAll('[role="checkbox"]')
    expect(boxes[0].attributes('aria-checked')).toBe('false')
    expect(boxes[1].attributes('aria-checked')).toBe('true')

    await w.setProps({ modelValue: ['a'] })
    expect(boxes[0].attributes('aria-checked')).toBe('true')
    expect(boxes[1].attributes('aria-checked')).toBe('false')
  })

  it('propagates disabled to every checkbox, not just the first', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: [], disabled: true } })
    const boxes = w.findAll('[role="checkbox"]')
    expect(boxes).toHaveLength(2)
    for (const box of boxes) expect(box.attributes('disabled')).toBe('')
  })

  // Exercises the vendored child's own emit directly rather than through a simulated DOM click,
  // pinning the same @update:model-value listener the click-driven tests above exercise indirectly
  // through reka's CheckboxRoot handleClick.
  it('relays the toggled array when the vendored child emits, wiring the template listener itself', async () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: ['a'] } })
    await w.findAllComponents({ name: 'Checkbox' })[1].vm.$emit('update:modelValue', true)
    expect(w.emitted('update:modelValue')?.[0]).toEqual([['a', 'b']])
  })

  // reka derives each checkbox's accessible name from `document.querySelector('[for="${id}"]')` when
  // no aria-label is set, and the visual click target is the whole label text via `for`; both depend
  // on this explicit for/id pairing, not on DOM nesting (Checkbox and Label are siblings, not parent
  // and child). Asserting non-empty AND equal so the check cannot pass with both sides blanked out.
  it('associates each label with its checkbox via explicit for/id', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options: twoOptions }), modelValue: [] } })
    const labels = w.findAll('label')
    const boxes = w.findAll('[role="checkbox"]')
    expect(labels).toHaveLength(2)
    labels.forEach((label, i) => {
      const forAttr = label.attributes('for')
      expect(forAttr).toBeTruthy()
      expect(forAttr).toBe(boxes[i].attributes('id'))
    })
  })

  // RepeaterField renders this component once per row, as siblings inside one Vue app tree — not as
  // separate mounts — with the same field.name. useId()'s counter is scoped to a single app
  // instance, so two independent mount() calls would each start their own app and both start
  // counting from zero, defeating this exact assertion; the host component below renders two
  // CheckboxGroupField as siblings in one tree to match the real repeater shape.
  it('scopes ids per component instance, not just per option', () => {
    const f = field({ interface: 'checkboxGroup', options: [{ value: 'a', label: 'Alpha' }] })
    const Host = defineComponent({
      render: () => h('div', [h(CheckboxGroupField, { field: f, modelValue: [] }), h(CheckboxGroupField, { field: f, modelValue: [] })]),
    })
    const w = mount(Host)
    const boxes = w.findAll('[role="checkbox"]')
    expect(boxes).toHaveLength(2)
    expect(boxes[0].attributes('id')).toBeTruthy()
    expect(boxes[0].attributes('id')).not.toBe(boxes[1].attributes('id'))
  })
})
