import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import TableFooter from './TableFooter.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { collectionList: { range: 'Showing {from}–{to} of {total}' } } },
})

function mountTF(props: { first: number; rows: number; total: number }) {
  return mount(TableFooter, { props, global: { plugins: [i18n] } })
}

describe('TableFooter', () => {
  it('renders the 1-based range for a full first page', () => {
    expect(mountTF({ first: 0, rows: 25, total: 128 }).text()).toBe('Showing 1–25 of 128')
  })
  it('clamps `to` to total on a partial last page', () => {
    // NOTE: brief's original input was { first: 100, rows: 25, total: 128 }, but
    // first(100) + rows(25) = 125 never exceeds total(128), so `to` (per the stated
    // formula `Math.min(first + rows, total)`) would be 125, not 128 — the clamp this
    // test claims to exercise never actually triggers. rows bumped to 30 so
    // first + rows (130) exceeds total and the clamp fires, while keeping the
    // brief's expected output string unchanged.
    expect(mountTF({ first: 100, rows: 30, total: 128 }).text()).toBe('Showing 101–128 of 128')
  })
  it('renders 0–0 of 0 when there are no records', () => {
    expect(mountTF({ first: 0, rows: 25, total: 0 }).text()).toBe('Showing 0–0 of 0')
  })
})
