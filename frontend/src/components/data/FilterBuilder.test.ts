import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
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
  it('applies on Enter in a text value field, via real input + keyup events', async () => {
    const w = mountBuilder()
    await w.get('[data-testid="filter-add"]').trigger('click')
    await w.vm.setDraftField(0, 'title')
    const valueInput = w.get('[aria-label="Value"]')
    await valueInput.setValue('hello')
    await valueInput.trigger('keyup.enter')
    expect(w.emitted('apply')![0][0]).toEqual({ title: { op: '_eq', value: 'hello' } })
  })
})
