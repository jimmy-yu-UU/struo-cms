import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaFileList from './MediaFileList.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    colName: 'Name', colType: 'Type', colSize: 'Size', colDimensions: 'Dimensions', colUploaded: 'Uploaded',
    colTypeFolder: 'Folder', folderRename: 'Rename folder', folderDelete: 'Delete folder',
  } } },
})

const files = [
  { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024, width: 800, height: 600, createdAt: '2026-07-01T00:00:00Z' },
  { id: 'f2', fileName: 'b.pdf', contentType: 'application/pdf', size: 2048 },
]

const folders = [{ id: 'd1', name: 'Docs', parentId: null }]

function mountList() {
  return mount(MediaFileList, { props: { files }, global: { plugins: [i18n] } })
}

describe('MediaFileList', () => {
  it('renders one row per file', () => {
    const w = mountList()
    expect(w.findAll('.media-list__row')).toHaveLength(2)
  })
  it('gives each row thumbnail the sm size', () => {
    const w = mountList()
    const thumbs = w.findAll('.file-thumb')
    expect(thumbs.length).toBeGreaterThan(0)
    for (const thumb of thumbs) expect(thumb.attributes('data-size')).toBe('sm')
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

  it('renders the actions slot once per file', () => {
    const w = mount(MediaFileList, {
      props: { files },
      slots: { actions: '<button class="act">{{ params.file.id }}</button>' },
      global: { plugins: [i18n] },
    })
    expect(w.findAll('.act')).toHaveLength(files.length)
  })

  it('renders folder rows ahead of file rows', () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n] } })
    const rows = w.findAll('tbody tr')
    expect(rows).toHaveLength(folders.length + files.length)
    expect(rows[0].text()).toContain('Docs')
    expect(rows[0].text()).toContain('Folder')
  })

  it('emits openFolder when a folder row name is activated', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n] } })
    await w.find('tbody tr .media-list__open').trigger('click')
    expect(w.emitted('openFolder')?.[0]).toEqual(['d1'])
  })

  it('renders no folder rows when none are passed', () => {
    const w = mount(MediaFileList, { props: { files }, global: { plugins: [i18n] } })
    expect(w.findAll('tbody tr')).toHaveLength(files.length)
  })
})
