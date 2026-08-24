import { describe, it, expect, afterEach } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextTableGrid from './RichTextTableGrid.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { richtext: {
    table: 'Table',
    tableSizeCols: '{count} column | {count} columns',
    tableSizeRows: '{count} row | {count} rows',
  } } } },
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
    // A count of 80 alone would also pass an 8-column by 10-row grid (transposed) -- these two
    // corners are only both present in the intended 10-wide by 8-tall layout.
    expect(w.find('[data-cell="8-10"]').exists()).toBe(true)
    expect(w.find('[data-cell="9-1"]').exists()).toBe(false)
  })

  // ARIA requires a grid's cells to be owned by role="row" children rather than handed to the
  // grid directly, which is the whole reason the eight `class="contents"` wrappers exist (the
  // alternative considered was downgrading the widget to role="group"). Deleting those wrappers
  // leaves every other assertion in this file true, so without this the semantics are unguarded
  // -- and this is a template, so a fork WILL edit this component.
  it('owns its cells through eight role="row" wrappers', () => {
    w = build()
    const rows = w.findAll('[role="row"]')
    expect(rows.length).toBe(8)
    // Scoped to the first row, not the grid: 80 gridcells anywhere under the grid would also be
    // true of cells parented directly by it, which is the arrangement this test exists to reject.
    expect(rows[0].findAll('[role="gridcell"]').length).toBe(10)
  })

  // The accessible name is deliberately the STATIC widget name, with the live size linked as a
  // description instead -- see the component's own comment for why. Both halves are asserted
  // because either one alone can rot: moving the size into aria-label, or dropping the
  // aria-describedby link, each breaks the split without breaking the other attribute.
  it('names itself statically and describes itself with the live readout', async () => {
    w = build()
    const grid = w.get('[role="grid"]')
    const readoutId = w.get('[data-testid="table-size-readout"]').attributes('id')
    // Without this, a future useId() returning undefined would make the equality below hold
    // between two undefineds even with the aria-describedby binding deleted outright.
    expect(readoutId).toBeTruthy()
    expect(grid.attributes('aria-describedby')).toBe(readoutId)
    expect(grid.attributes('aria-label')).toBe('Table')
    // Static means static: picking a size must not rewrite the name.
    await w.get('[data-cell="3-4"]').trigger('mouseenter')
    expect(grid.attributes('aria-label')).toBe('Table')
  })

  it('shows no highlight or readout before the first hover or key press', () => {
    w = build()
    expect(w.findAll('[data-cell][data-in-range="true"]').length).toBe(0)
    // A readout that already says "1 column x 1 row" before any interaction would tell a
    // sighted user (and, worse, a screen-reader user who has only just landed on the grid) that a
    // size has already been chosen -- the very thing the no-highlight rule above exists to avoid.
    expect(w.get('[data-testid="table-size-readout"]').text()).toBe('')
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

  it('moves one step at a time with the arrow keys and confirms with Enter', async () => {
    w = build()
    const grid = w.get('[role="grid"]')
    // Single-axis first: ArrowRight+ArrowDown together would land on 2x2, a symmetric result that
    // a handler mapping ArrowRight to a row move (instead of a column move) would also produce.
    // Checking after just the ArrowRight catches that swap.
    await grid.trigger('keydown', { key: 'ArrowRight' })
    expect(w.get('[data-testid="table-size-readout"]').text()).toBe('2 columns × 1 row')
    await grid.trigger('keydown', { key: 'ArrowDown' })
    expect(w.get('[data-testid="table-size-readout"]').text()).toBe('2 columns × 2 rows')
    await grid.trigger('keydown', { key: 'Enter' })
    expect(w.emitted('pick')).toEqual([[{ rows: 2, cols: 2 }]])
  })

  it('confirms the current cursor with Space as well as Enter', async () => {
    w = build()
    const grid = w.get('[role="grid"]')
    await grid.trigger('keydown', { key: 'ArrowRight' })
    await grid.trigger('keydown', { key: ' ' })
    expect(w.emitted('pick')).toEqual([[{ rows: 1, cols: 2 }]])
  })

  it('does not move past the low grid edges', async () => {
    w = build()
    const grid = w.get('[role="grid"]')
    await grid.trigger('keydown', { key: 'ArrowLeft' })
    await grid.trigger('keydown', { key: 'ArrowUp' })
    expect(w.get('[data-testid="table-size-readout"]').text()).toBe('1 column × 1 row')
  })

  it('does not move past the high grid edges', async () => {
    w = build()
    const grid = w.get('[role="grid"]')
    // Twelve presses of each is more than enough to reach both a 10-wide and an 8-tall ceiling
    // from a 1x1 start; a ROWS/COLS swap in the ceiling clamp would let this claim a 9- or
    // 10-row table that no cell in the rendered 8-row grid can actually reach.
    for (let i = 0; i < 12; i++) {
      await grid.trigger('keydown', { key: 'ArrowRight' })
      await grid.trigger('keydown', { key: 'ArrowDown' })
    }
    expect(w.get('[data-testid="table-size-readout"]').text()).toBe('10 columns × 8 rows')
  })
})
