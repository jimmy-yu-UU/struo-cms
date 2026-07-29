import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaFileList from './MediaFileList.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    colName: 'Name', colType: 'Type', colSize: 'Size', colDimensions: 'Dimensions', colUploaded: 'Uploaded',
  } } },
})

const files = [
  { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024, width: 800, height: 600, createdAt: '2026-07-01T00:00:00Z' },
  { id: 'f2', fileName: 'b.pdf', contentType: 'application/pdf', size: 2048 },
]

function mountList() {
  return mount(MediaFileList, { props: { files }, global: { plugins: [i18n] } })
}

describe('MediaFileList', () => {
  it('renders one row per file', () => {
    const w = mountList()
    expect(w.findAll('.media-list__row')).toHaveLength(2)
  })
  it('shows a human-readable size', () => {
    const w = mountList()
    expect(w.text()).toContain('1.0 KB')
  })
  it('emits open with the id on row click', async () => {
    const w = mountList()
    await w.findAll('.media-list__row')[0].trigger('click')
    expect(w.emitted('open')?.[0]).toEqual(['f1'])
  })

  it('exposes a real, focusable <button> instead of an ARIA-button row hack', () => {
    const w = mountList()
    const row = w.findAll('.media-list__row')[0]
    // A <tr> is not a button; screen readers get contradictory roles when it claims to be one.
    // Table semantics must stay intact -- no role/tabindex overrides on the row itself.
    expect(row.attributes('role')).toBeUndefined()
    expect(row.attributes('tabindex')).toBeUndefined()
    const btn = row.find('button')
    expect(btn.exists()).toBe(true)
    expect(btn.element.tagName).toBe('BUTTON')
  })

  it('emits open when the row button is activated', async () => {
    const w = mountList()
    const btn = w.findAll('.media-list__row')[0].find('button')
    // Real <button> elements are natively keyboard-activatable: the HTML spec guarantees Enter
    // and Space dispatch a click. jsdom doesn't simulate that browser default action, so we
    // assert the click the browser dispatches on activation -- backed by the real <button> tag
    // assertion above, which is what actually gives us Enter/Space + tab-order for free.
    await btn.trigger('click')
    expect(w.emitted('open')?.[0]).toEqual(['f1'])
  })

  it('still opens on a plain row click outside the button (visible click behavior unchanged)', async () => {
    const w = mountList()
    const row = w.findAll('.media-list__row')[0]
    await row.find('td.media-list__thumb').trigger('click')
    expect(w.emitted('open')?.[0]).toEqual(['f1'])
  })

  it('does not double-emit when the button itself is clicked', async () => {
    const w = mountList()
    const btn = w.findAll('.media-list__row')[0].find('button')
    await btn.trigger('click')
    expect(w.emitted('open')).toEqual([['f1']])
  })
})
