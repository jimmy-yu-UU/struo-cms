import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import DataTablePagination from './DataTablePagination.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountPager(props: { page: number; pageSize: number; total: number; pageSizeOptions?: number[]; showPageSizeSelector?: boolean }) {
  return mount(DataTablePagination, {
    props,
    // reka-ui's Select portal is itself named "Teleport" -- see vitest.setup.ts for why this stub
    // configuration is required, and UiLanguageSwitcher.test.ts for the same pattern applied to
    // the same vendored Select.
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

// reka's SelectTrigger opens on pointerdown, not click, and its SelectItems select on pointerup --
// see vitest.setup.ts's own comment plus UiLanguageSwitcher.test.ts, which drives the exact same
// vendored Select this way already.
async function openPageSizeSelect(w: ReturnType<typeof mountPager>) {
  await w.get('[data-testid="page-size-select"]').trigger('pointerdown')
  await flushPromises()
}

// en.ts: collectionList.range === 'Showing {from}–{to} of {total}'. Assert the whole rendered
// string — a `toContain('1')` would pass on almost any output.
describe('DataTablePagination', () => {
  it('renders a 1-based human range for the first page', () => {
    expect(mountPager({ page: 0, pageSize: 25, total: 60 }).text()).toContain('Showing 1–25 of 60')
  })

  it('clamps the range end to the total on the last page', () => {
    expect(mountPager({ page: 2, pageSize: 25, total: 60 }).text()).toContain('Showing 51–60 of 60')
  })

  it('renders a zero range when there is nothing to show', () => {
    expect(mountPager({ page: 0, pageSize: 25, total: 0 }).text()).toContain('Showing 0–0 of 0')
  })

  it('emits the next 0-based page index', async () => {
    const w = mountPager({ page: 0, pageSize: 25, total: 60 })
    const next = w.findAll('button').find((b) => b.attributes('data-testid') === 'pagination-next')
    expect(next).toBeDefined()
    await next!.trigger('click')
    expect(w.emitted('update:page')![0][0]).toBe(1)
  })

  it('disables previous on the first page and next on the last', () => {
    const first = mountPager({ page: 0, pageSize: 25, total: 60 })
    expect(first.find('[data-testid="pagination-prev"]').attributes('disabled')).toBeDefined()
    const last = mountPager({ page: 2, pageSize: 25, total: 60 })
    expect(last.find('[data-testid="pagination-next"]').attributes('disabled')).toBeDefined()
  })

  it('shows the current page size as the trigger label', async () => {
    const w = mountPager({ page: 0, pageSize: 50, total: 60 })
    // SelectValue's label lookup depends on SelectItem registration, which happens one tick after
    // the initial synchronous render (see UiLanguageSwitcher.test.ts's identical comment).
    await flushPromises()
    expect(w.get('[data-testid="page-size-select"]').text()).toBe('50')
  })

  it('offers exactly the 10/25/50/100 page-size options', async () => {
    const w = mountPager({ page: 0, pageSize: 25, total: 60 })
    await openPageSizeSelect(w)
    const values = w.findAll('[role="option"]').map((o) => o.text())
    expect(values).toEqual(['10', '25', '50', '100'])
  })

  // The one test in this file that drives the real widget end to end, not just the component
  // boundary: opens the actual listbox and fires a pointerup on a real SelectItem, proving the
  // control the user sees is actually wired up, not just the props/events contract around it.
  it('emits the newly chosen page size as a number when an option is picked from the open listbox', async () => {
    const w = mountPager({ page: 0, pageSize: 25, total: 60 })
    await openPageSizeSelect(w)
    const option50 = w.findAll('[role="option"]').find((o) => o.text() === '50')
    expect(option50).toBeDefined()
    await option50!.trigger('pointerup')
    await flushPromises()
    expect(w.emitted('update:pageSize')![0][0]).toBe(50)
  })

  // See the pageSizeOptions prop's own JSDoc for why an override matters here.
  it('accepts a pageSizeOptions override for a caller with different defaults', async () => {
    const w = mountPager({ page: 0, pageSize: 5, total: 12, pageSizeOptions: [5, 10, 20] })
    await flushPromises()
    expect(w.get('[data-testid="page-size-select"]').text()).toBe('5')
    await openPageSizeSelect(w)
    const values = w.findAll('[role="option"]').map((o) => o.text())
    expect(values).toEqual(['5', '10', '20'])
  })

  // Pins the pageSizeOptions JSDoc's documented out-of-range behaviour: a pageSize absent from the
  // offered options leaves the trigger blank rather than falling back to some option's label.
  it('shows a blank trigger label when pageSize matches none of the offered options', async () => {
    const w = mountPager({ page: 0, pageSize: 24, total: 60, pageSizeOptions: [10, 25, 50, 100] })
    await flushPromises()
    expect(w.get('[data-testid="page-size-select"]').text()).toBe('')
  })

  it('hides the rows-per-page control when showPageSizeSelector is false', () => {
    const w = mountPager({ page: 0, pageSize: 25, total: 60, showPageSizeSelector: false })
    expect(w.find('[data-testid="page-size-select"]').exists()).toBe(false)
  })
})
