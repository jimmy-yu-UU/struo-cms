import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import type { FilterSpec } from '@/lib/buildListQuery'
import FilterBuilder from './FilterBuilder.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const fields = [
  { name: 'title', label: 'Title' },
  { name: 'status', label: 'Status', options: [
    { label: 'Draft', value: 'draft' }, { label: 'Published', value: 'published' },
  ] },
]

function mountBuilder(applied = {}) {
  return mount(FilterBuilder, {
    props: { fields, applied },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

type Wrapper = ReturnType<typeof mountBuilder>
type Trigger = ReturnType<Wrapper['findAll']>[number]

// jsdom CAN dispatch reka-ui's pointerdown/pointerup (see vitest.setup.ts); driving the real
// interaction instead of emitting from the vendored Select child is what actually exercises this
// component's own @update:model-value listener -- emitting from the child never renders
// SelectContent/SelectItem at all, so a wrong :value or a missing/broken option list would stay
// green. Three Selects can render on one row, so the option lookup is scoped through the
// trigger's own aria-controls target rather than searched for across the whole document.
async function pickOption(w: Wrapper, trigger: Trigger, optionText: string): Promise<void> {
  await trigger.trigger('pointerdown')
  await flushPromises()
  const content = w.find(`#${trigger.attributes('aria-controls')}`)
  const option = content.findAll('[role="option"]').find((o) => o.text() === optionText)
  if (!option) throw new Error(`no option "${optionText}" under the opened listbox`)
  await option.trigger('pointerup')
  await flushPromises()
}

function selectTriggers(w: Wrapper, rowIndex: number): Trigger[] {
  return w.findAll('[data-testid="filter-row"]')[rowIndex].findAll('[data-slot="select-trigger"]')
}

describe('FilterBuilder', () => {
  it('starts with no condition rows', () => {
    expect(mountBuilder().findAll('[data-testid="filter-row"]')).toHaveLength(0)
  })

  it('adds and removes condition rows', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    expect(w.findAll('[data-testid="filter-row"]')).toHaveLength(1)
    await w.get('[data-testid="filter-remove"]').trigger('click')
    expect(w.findAll('[data-testid="filter-row"]')).toHaveLength(0)
  })

  // The core contract: editing must NOT reach the consumer. Only apply does.
  it('does not emit while a draft condition is being edited', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.get('[aria-label="Value"]').setValue('hello')
    expect(w.emitted('apply')).toBeUndefined()
  })

  it('emits a FilterSpec only when apply is pressed', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await pickOption(w, selectTriggers(w, 0)[1], 'contains')
    await w.get('[aria-label="Value"]').setValue('hello')
    await w.get('[data-testid="filter-apply"]').trigger('click')
    expect(w.emitted('apply')![0][0]).toEqual({ title: { op: '_contains', value: 'hello' } })
  })

  it('drops rows with an empty value so a half-typed condition never filters', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.get('[data-testid="filter-apply"]').trigger('click')
    expect(w.emitted('apply')![0][0]).toEqual({})
  })

  it('clears the draft and applies an empty spec', async () => {
    const w = mountBuilder({ title: { op: '_eq', value: 'x' } })
    await w.get('[data-testid="filter-clear"]').trigger('click')
    expect(w.emitted('apply')![0][0]).toEqual({})
    expect(w.findAll('[data-testid="filter-row"]')).toHaveLength(0)
  })

  // FilterSpec is Record<field, …>: a second condition on the same field would silently
  // overwrite the first, so the field picker must not offer an already-used field.
  it('excludes already-used fields from a row\'s field options', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click') // row 0 defaults to 'title'
    await w.get('[data-testid="filter-add"]').trigger('click') // row 1: only 'status' is free
    const trigger = selectTriggers(w, 1)[0]
    await trigger.trigger('pointerdown')
    await flushPromises()
    const offered = w.find(`#${trigger.attributes('aria-controls')}`)
      .findAll('[role="option"]').map((o) => o.text())
    expect(offered).toEqual(['Status'])
  })

  it('hydrates its draft from the applied spec on mount', () => {
    const w = mountBuilder({ status: { op: '_eq', value: 'draft' } })
    expect(w.findAll('[data-testid="filter-row"]')).toHaveLength(1)
  })

  // Real-DOM coverage for the Enter-to-apply path: the value is typed into the plain text Input
  // and applied via a genuine keyup.enter, not through any test-only shortcut.
  //
  // The intermediate assertion is load-bearing: without it, a regression that wires apply()
  // into the Input's @update:model-value binding (so it fires on every keystroke, not just
  // Enter) would still pass — setValue would emit the identical payload at emitted('apply')[0],
  // and the keyup.enter afterwards would just add a second, unchecked emit.
  it('applies on Enter in a text value field, via real input + keyup events', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    const valueInput = w.get('[aria-label="Value"]')
    await valueInput.setValue('hello')
    expect(w.emitted('apply')).toBeUndefined()
    await valueInput.trigger('keyup.enter')
    expect(w.emitted('apply')![0][0]).toEqual({ title: { op: '_eq', value: 'hello' } })
  })

  // Simulates the exact echo hazard FilterBuilder.vue's `watch(() => props.applied, ...)` comment
  // describes: the consumer re-assigns `applied` from the just-emitted spec, and a naive
  // hydrate() would wholesale-replace the draft, silently deleting a still-mid-edit row.
  it('keeps an in-progress row when the consumer echoes the just-emitted spec back', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.get('[aria-label="Value"]').setValue('hello')
    await w.get('[data-testid="filter-add"]').trigger('click') // row 1: status, no value yet
    await w.get('[data-testid="filter-apply"]').trigger('click')
    const emittedSpec = w.emitted('apply')![0][0] as FilterSpec
    expect(emittedSpec).toEqual({ title: { op: '_eq', value: 'hello' } })

    await w.setProps({ applied: emittedSpec })
    expect(w.findAll('[data-testid="filter-row"]')).toHaveLength(2)
  })

  // Regression for the degenerate case named alongside the above: adding a row and pressing
  // Search before typing anything emits {} — if the consumer echoes that back verbatim, the
  // row just added must not vanish.
  it('keeps a freshly-added empty row when the consumer echoes an empty applied spec back', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.get('[data-testid="filter-apply"]').trigger('click')
    expect(w.emitted('apply')![0][0]).toEqual({})

    await w.setProps({ applied: {} })
    expect(w.findAll('[data-testid="filter-row"]')).toHaveLength(1)
  })

  // canAdd must ask "is there a free field?", not "is draft shorter than fields?" — those
  // diverge the moment a hydrated row names a field absent from `fields` (e.g. a field removed
  // from the collection schema after the filter was applied).
  it('still allows adding a row when a hydrated row references a field outside `fields`', async () => {
    const w = mount(FilterBuilder, {
      props: { fields, applied: { ghost: { op: '_eq', value: 'x' } } },
      global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    await w.get('[data-testid="filter-add"]').trigger('click')
    expect(w.findAll('[data-testid="filter-row"]')).toHaveLength(2)
    expect(w.get('[data-testid="filter-add"]').attributes('disabled')).toBeUndefined()
  })

  it('trims whitespace from a value before it reaches the emitted FilterSpec', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.get('[aria-label="Value"]').setValue('  hello  ')
    await w.get('[data-testid="filter-apply"]').trigger('click')
    expect(w.emitted('apply')![0][0]).toEqual({ title: { op: '_eq', value: 'hello' } })
  })

  // Proves the field Select's own @update:model-value listener, not just its default: addRow
  // defaults a fresh row to 'title' (the first free field), so re-picking 'Status' can only
  // succeed if the trigger's real listener runs. Picking the default value back would not catch
  // a deleted listener, since the draft would already hold that value regardless.
  it('stores a field pick made through the real Select trigger and its listbox', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await pickOption(w, selectTriggers(w, 0)[0], 'Status')
    await pickOption(w, selectTriggers(w, 0)[2], 'Draft')
    await w.get('[data-testid="filter-apply"]').trigger('click')
    expect(w.emitted('apply')![0][0]).toEqual({ status: { op: '_eq', value: 'draft' } })
  })

  // Proves the operator Select's own @update:model-value listener: '_contains' is not the
  // default '_eq', so this can only pass if the pick actually reached the draft.
  it('stores an operator pick made through the real Select trigger and its listbox', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await pickOption(w, selectTriggers(w, 0)[1], 'contains')
    await w.get('[aria-label="Value"]').setValue('hello')
    await w.get('[data-testid="filter-apply"]').trigger('click')
    expect(w.emitted('apply')![0][0]).toEqual({ title: { op: '_contains', value: 'hello' } })
  })

  // With an enumerated field selected the value control is a third Select, not an Input — a
  // branch no test in this file has ever rendered before. The length assertion is the direct
  // regression guard for that: it can only be 3 if this branch actually rendered.
  it('renders and stores an enumerated value pick made through the real Select trigger', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await pickOption(w, selectTriggers(w, 0)[0], 'Status')
    expect(selectTriggers(w, 0)).toHaveLength(3)
    await pickOption(w, selectTriggers(w, 0)[2], 'Published')
    await w.get('[data-testid="filter-apply"]').trigger('click')
    expect(w.emitted('apply')![0][0]).toEqual({ status: { op: '_eq', value: 'published' } })
  })

  // The inbound half: a trigger that does not render the stored value means the binding is
  // one-way (write-only). Both picks are deliberately non-default so this cannot pass by
  // accident from the initial render.
  it('shows the picked field and operator on their triggers', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await pickOption(w, selectTriggers(w, 0)[0], 'Status')
    await pickOption(w, selectTriggers(w, 0)[1], 'contains')
    expect(selectTriggers(w, 0)[0].text()).toContain('Status')
    expect(selectTriggers(w, 0)[1].text()).toContain('contains')
  })

  // `wrapper.vm` is not the right instrument here: Vue Test Utils' vm proxy reads through
  // `instance.setupState` for any key `defineExpose` did not list, so `wrapper.vm.setDraftField`
  // stays truthy with or without the expose block. `instance.$.exposed` is what `defineExpose`
  // actually controls (the compiler calls `expose({})` for every `<script setup>` component, so
  // an empty object — not undefined — is the "nothing exposed" state).
  it('exposes nothing: the component has no test-only surface', () => {
    const w = mountBuilder()
    expect(Object.keys(w.vm.$.exposed ?? {})).toEqual([])
  })
})
