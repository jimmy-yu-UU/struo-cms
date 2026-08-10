import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
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
  return mount(FilterBuilder, { props: { fields, applied }, global: { plugins: [i18n] } })
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
    await w.vm.setDraftValue(0, 'hello')
    expect(w.emitted('apply')).toBeUndefined()
  })

  it('emits a FilterSpec only when apply is pressed', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.vm.setDraftField(0, 'title')
    await w.vm.setDraftOperator(0, '_contains')
    await w.vm.setDraftValue(0, 'hello')
    await w.get('[data-testid="filter-apply"]').trigger('click')
    expect(w.emitted('apply')![0][0]).toEqual({ title: { op: '_contains', value: 'hello' } })
  })

  it('drops rows with an empty value so a half-typed condition never filters', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.vm.setDraftField(0, 'title')
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
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.vm.setDraftField(0, 'title')
    await w.get('[data-testid="filter-add"]').trigger('click')
    expect(w.vm.fieldOptionsFor(1).map((f: { name: string }) => f.name)).toEqual(['status'])
  })

  it('hydrates its draft from the applied spec on mount', () => {
    const w = mountBuilder({ status: { op: '_eq', value: 'draft' } })
    expect(w.findAll('[data-testid="filter-row"]')).toHaveLength(1)
  })

  // Real-DOM coverage for the Enter-to-apply path: setDraftField uses the exposed API (reka's
  // Select can't be driven by click under jsdom — see the other tests), but the value itself is
  // typed into the plain text Input and applied via a genuine keyup.enter, not through the
  // exposed setters.
  //
  // The intermediate assertion is load-bearing: without it, a regression that wires apply()
  // into the Input's @update:model-value binding (so it fires on every keystroke, not just
  // Enter) would still pass — setValue would emit the identical payload at emitted('apply')[0],
  // and the keyup.enter afterwards would just add a second, unchecked emit.
  it('applies on Enter in a text value field, via real input + keyup events', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.vm.setDraftField(0, 'title')
    const valueInput = w.get('[aria-label="Value"]')
    await valueInput.setValue('hello')
    expect(w.emitted('apply')).toBeUndefined()
    await valueInput.trigger('keyup.enter')
    expect(w.emitted('apply')![0][0]).toEqual({ title: { op: '_eq', value: 'hello' } })
  })

  // Task 14 will re-assign `applied` from the spec FilterBuilder itself just emitted (or from a
  // route-query computed that mints a new object per navigation). `watch(..., { deep: true })`
  // fires on that re-assignment even though the spec is unchanged, and a naive hydrate() would
  // wholesale-replace the draft — silently deleting any row still mid-edit that toSpec() had
  // dropped as half-authored. Simulate exactly that echo.
  it('keeps an in-progress row when the consumer echoes the just-emitted spec back', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.vm.setDraftField(0, 'title')
    await w.vm.setDraftValue(0, 'hello')
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
      global: { plugins: [i18n] },
    })
    await w.get('[data-testid="filter-add"]').trigger('click')
    expect(w.findAll('[data-testid="filter-row"]')).toHaveLength(2)
    expect(w.get('[data-testid="filter-add"]').attributes('disabled')).toBeUndefined()
  })

  it('trims whitespace from a value before it reaches the emitted FilterSpec', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.vm.setDraftField(0, 'title')
    await w.vm.setDraftValue(0, '  hello  ')
    await w.get('[data-testid="filter-apply"]').trigger('click')
    expect(w.emitted('apply')![0][0]).toEqual({ title: { op: '_eq', value: 'hello' } })
  })
})
