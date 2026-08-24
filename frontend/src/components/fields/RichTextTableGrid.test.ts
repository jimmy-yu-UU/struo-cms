import { describe, it, expect, afterEach } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextTableGrid from './RichTextTableGrid.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { richtext: { tableSize: '{cols} columns × {rows} rows' } } } },
})

let w: VueWrapper | null = null
afterEach(() => { w?.unmount(); w = null })

function build(): VueWrapper {
  return mount(RichTextTableGrid, { global: { plugins: [i18n] } })
}

describe('RichTextTableGrid', () => {
  it('renders a 10 by 8 grid', () => {
    w = build()
    expect(w.findAll('[data-cell]').length).toBe(80)
  })

  it('shows no highlight before the first hover or key press', () => {
    w = build()
    expect(w.findAll('[data-cell][data-in-range="true"]').length).toBe(0)
  })

  it('highlights the hovered rectangle and reads out its size', async () => {
    w = build()
    await w.get('[data-cell="3-4"]').trigger('mouseenter')
    expect(w.findAll('[data-cell][data-in-range="true"]').length).toBe(12)
    expect(w.get('[data-testid="table-size-readout"]').text()).toBe('4 columns × 3 rows')
  })

  it('emits the hovered size on click', async () => {
    w = build()
    await w.get('[data-cell="2-3"]').trigger('mouseenter')
    await w.get('[data-cell="2-3"]').trigger('click')
    expect(w.emitted('pick')).toEqual([[{ rows: 2, cols: 3 }]])
  })

  it('moves with the arrow keys and confirms with Enter', async () => {
    w = build()
    const grid = w.get('[role="grid"]')
    await grid.trigger('keydown', { key: 'ArrowRight' })
    await grid.trigger('keydown', { key: 'ArrowDown' })
    expect(w.get('[data-testid="table-size-readout"]').text()).toBe('2 columns × 2 rows')
    await grid.trigger('keydown', { key: 'Enter' })
    expect(w.emitted('pick')).toEqual([[{ rows: 2, cols: 2 }]])
  })

  it('does not move past the grid edges', async () => {
    w = build()
    const grid = w.get('[role="grid"]')
    await grid.trigger('keydown', { key: 'ArrowLeft' })
    await grid.trigger('keydown', { key: 'ArrowUp' })
    expect(w.get('[data-testid="table-size-readout"]').text()).toBe('1 columns × 1 rows')
  })
})
