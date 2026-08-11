import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import SortableList from './SortableList.vue'
import en from '@/locales/en'
import zhTW from '@/locales/zh-TW'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const opts = { global: { plugins: [i18n] } }

type Row = { id: string; name: string }
const ROWS: Row[] = [{ id: 'a', name: 'Alpha' }, { id: 'b', name: 'Beta' }, { id: 'c', name: 'Gamma' }]

// SortableList is a generic SFC; instantiate it with a concrete row type via a TS instantiation
// expression so `mount` sees non-generic props instead of failing to satisfy
// `abstract new (...) => any` against the generic component signature.
const RowSortableList = SortableList<Row>

function mountList(rows: Row[] = ROWS, disabled = false) {
  return mount(RowSortableList, {
    props: { modelValue: rows, itemKey: (r: Row) => r.id, disabled },
    slots: { item: '<template #item="{ item }"><span class="row-name">{{ item.name }}</span></template>' },
    ...opts,
  })
}

describe('SortableList', () => {
  it('renders the caller slot once per item, in order', () => {
    const w = mountList()
    expect(w.findAll('.row-name').map((n) => n.text())).toEqual(['Alpha', 'Beta', 'Gamma'])
  })

  it('moves an item up and emits a new array', async () => {
    const before = [...ROWS]
    const w = mountList(before)
    await w.findAll('[data-testid="move-up"]')[1].trigger('click')
    expect((w.emitted('update:modelValue')?.[0][0] as Row[]).map((r) => r.id)).toEqual(['b', 'a', 'c'])
    // The form model is snapshot-compared for dirtiness (lib/formDirty.ts); mutating in place
    // would make a reorder invisible to that comparison.
    expect(before.map((r) => r.id)).toEqual(['a', 'b', 'c'])
  })

  it('moves an item down', async () => {
    const w = mountList()
    await w.findAll('[data-testid="move-down"]')[0].trigger('click')
    expect((w.emitted('update:modelValue')?.[0][0] as Row[]).map((r) => r.id)).toEqual(['b', 'a', 'c'])
  })

  it('disables up on the first row and down on the last', () => {
    const w = mountList()
    expect(w.findAll('[data-testid="move-up"]')[0].attributes('disabled')).toBeDefined()
    expect(w.findAll('[data-testid="move-down"]')[2].attributes('disabled')).toBeDefined()
  })

  it('emits nothing when a boundary control is clicked', async () => {
    const w = mountList()
    await w.findAll('[data-testid="move-up"]')[0].trigger('click')
    expect(w.emitted('update:modelValue')).toBeUndefined()
  })

  it('disables every control when the list is disabled', () => {
    const w = mountList(ROWS, true)
    for (const btn of w.findAll('button')) expect(btn.attributes('disabled')).toBeDefined()
  })

  it('names both controls for assistive tech', () => {
    const w = mountList()
    expect(w.findAll('[data-testid="move-up"]')[1].attributes('aria-label')).toBe(en.fields.moveUp)
    expect(w.findAll('[data-testid="move-down"]')[0].attributes('aria-label')).toBe(en.fields.moveDown)
  })

  it('names both controls under zh-TW too', () => {
    const zhI18n = createI18n({ legacy: false, locale: 'zh-TW', messages: { 'zh-TW': zhTW } })
    const w = mount(RowSortableList, {
      props: { modelValue: ROWS, itemKey: (r: Row) => r.id },
      slots: { item: '<template #item="{ item }"><span class="row-name">{{ item.name }}</span></template>' },
      global: { plugins: [zhI18n] },
    })
    expect(w.findAll('[data-testid="move-up"]')[1].attributes('aria-label')).toBe(zhTW.fields.moveUp)
    expect(w.findAll('[data-testid="move-down"]')[0].attributes('aria-label')).toBe(zhTW.fields.moveDown)
  })

  it('both controls are type="button" so they cannot submit an enclosing form', () => {
    const w = mountList()
    for (const btn of w.findAll('button')) expect(btn.attributes('type')).toBe('button')
  })

  it('breaks when the emit path is disconnected, proving the test can fail', async () => {
    // Sanity check for the suite itself, not the component: an intentionally wrong expectation
    // must fail, or the earlier assertions would be meaningless.
    const w = mountList()
    await w.findAll('[data-testid="move-down"]')[0].trigger('click')
    expect((w.emitted('update:modelValue')?.[0][0] as Row[]).map((r) => r.id)).not.toEqual(['a', 'b', 'c'])
  })
})
