import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import DataTablePagination from './DataTablePagination.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountPager(props: { page: number; pageSize: number; total: number }) {
  return mount(DataTablePagination, { props, global: { plugins: [i18n] } })
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

  it('shows the current page size as the selected option', () => {
    const w = mountPager({ page: 0, pageSize: 50, total: 60 })
    const select = w.get('[data-testid="page-size-select"]').element as HTMLSelectElement
    expect(select.value).toBe('50')
  })

  it('offers exactly the 10/25/50/100 page-size options', () => {
    const w = mountPager({ page: 0, pageSize: 25, total: 60 })
    const values = w.findAll('[data-testid="page-size-select"] option').map((o) => (o.element as HTMLOptionElement).value)
    expect(values).toEqual(['10', '25', '50', '100'])
  })

  it('emits the newly chosen page size as a number', async () => {
    const w = mountPager({ page: 0, pageSize: 25, total: 60 })
    await w.get('[data-testid="page-size-select"]').setValue('50')
    expect(w.emitted('update:pageSize')![0][0]).toBe(50)
  })
})
