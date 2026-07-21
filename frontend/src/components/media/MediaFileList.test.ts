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
})
