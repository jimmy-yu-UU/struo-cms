import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { createI18n } from 'vue-i18n'
import MediaFileList from './MediaFileList.vue'
import { DRAG_MIME, serializeMovePayload } from '../../lib/mediaMove'

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

  it('emits dropOn when a media drag is dropped on a folder row', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n] } })
    const payload = { files: ['f1'], folders: [] }
    const dataTransfer = {
      types: [DRAG_MIME],
      getData: (t: string) => (t === DRAG_MIME ? serializeMovePayload(payload) : ''),
      dropEffect: '',
    }
    await w.findAll('tbody tr')[0].trigger('drop', { dataTransfer })
    expect(w.emitted('dropOn')?.[0]).toEqual(['d1', payload])
  })

  it('does not make file rows drop targets', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n] } })
    const dataTransfer = {
      types: [DRAG_MIME],
      getData: () => serializeMovePayload({ files: ['f1'], folders: [] }),
      dropEffect: '',
    }
    await w.findAll('tbody tr')[folders.length].trigger('drop', { dataTransfer })
    expect(w.emitted('dropOn')).toBeUndefined()
  })

  it('ignores a drop carrying no media payload', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n] } })
    const dataTransfer = { types: ['text/plain'], getData: () => 'hello', dropEffect: '' }
    await w.findAll('tbody tr')[0].trigger('drop', { dataTransfer })
    expect(w.emitted('dropOn')).toBeUndefined()
  })

  // Permissions: folder moves require canWrite('mediafolder') -- without that grant the row must
  // not be a drag source at all, regardless of canManageFolders (which also covers delete-only
  // users who should still see rename/delete but not drag folders around).
  it('is not draggable when canMoveFolders is false or unset', () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n] } })
    expect(w.findAll('tbody tr')[0].attributes('draggable')).toBeUndefined()
  })

  it('is draggable and writes the folder payload on dragstart when canMoveFolders is true', async () => {
    const w = mount(MediaFileList, {
      props: { files, folders, canManageFolders: true, canMoveFolders: true },
      global: { plugins: [i18n] },
    })
    const row = w.findAll('tbody tr')[0]
    expect(row.attributes('draggable')).toBe('true')
    const setData = vi.fn()
    await row.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
    expect(setData).toHaveBeenCalledWith(DRAG_MIME, serializeMovePayload({ files: [], folders: ['d1'] }))
  })

  // Same bypass risk as MediaFolderCards/MediaGrid: `draggable` alone is not the gate. A bubbled
  // dragstart from inside a non-draggable row must still be refused by the handler itself.
  it('does not write a drag payload on dragstart when canMoveFolders is false (bubbled drag)', async () => {
    const w = mount(MediaFileList, {
      props: { files, folders, canManageFolders: true, canMoveFolders: false },
      global: { plugins: [i18n] },
    })
    const row = w.findAll('tbody tr')[0]
    const setData = vi.fn()
    await row.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
    expect(setData).not.toHaveBeenCalled()
  })

  // Permissions: file moves require canWrite('file') -- same reasoning, file rows.
  it('is not draggable when canMoveFiles is false or unset', () => {
    const w = mount(MediaFileList, { props: { files, folders }, global: { plugins: [i18n] } })
    expect(w.findAll('tbody tr')[folders.length].attributes('draggable')).toBeUndefined()
  })

  it('is draggable and writes the file payload on dragstart when canMoveFiles is true', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canMoveFiles: true }, global: { plugins: [i18n] } })
    const row = w.findAll('tbody tr')[folders.length]
    expect(row.attributes('draggable')).toBe('true')
    const setData = vi.fn()
    await row.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
    expect(setData).toHaveBeenCalledWith(DRAG_MIME, serializeMovePayload({ files: ['f1'], folders: [] }))
  })

  it('does not write a drag payload on dragstart when canMoveFiles is false (bubbled drag from the thumbnail)', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canMoveFiles: false }, global: { plugins: [i18n] } })
    const row = w.findAll('tbody tr')[folders.length]
    const setData = vi.fn()
    await row.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
    expect(setData).not.toHaveBeenCalled()
  })

  // dragleave follows the mouseout model (see MediaFolderCards): a row -> own-child crossing
  // targets the ROW itself with relatedTarget still inside it, and the highlight must survive
  // that; only a crossing whose relatedTarget has left the row entirely should clear it.
  it('keeps the drop-highlight when dragleave targets the row itself but relatedTarget is still inside it', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n] } })
    const row = w.findAll('tbody tr')[0]
    const dataTransfer = { types: [DRAG_MIME], getData: () => '', dropEffect: '' }
    await row.trigger('dragover', { dataTransfer })
    expect(row.attributes('data-dropping')).toBe('true')
    await row.trigger('dragleave', { relatedTarget: row.find('td').element })
    expect(row.attributes('data-dropping')).toBe('true')
  })

  it('clears the drop-highlight when dragleave bubbles from a child with relatedTarget outside the row', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n] } })
    const row = w.findAll('tbody tr')[0]
    const dataTransfer = { types: [DRAG_MIME], getData: () => '', dropEffect: '' }
    await row.trigger('dragover', { dataTransfer })
    expect(row.attributes('data-dropping')).toBe('true')
    await row.find('td').trigger('dragleave', { relatedTarget: null })
    expect(row.attributes('data-dropping')).toBeUndefined()
  })

  // A drag can end without ever reaching a drop (Esc, or a drop on a non-target) -- nothing else
  // resets the highlight in that case. `dragend` fires on the drag SOURCE, which may be a
  // different component entirely (a MediaGrid tile, or a MediaFolderCards card), so this must be
  // caught at the document level, not scoped to this row's own listeners.
  it('clears the drop-highlight when a drag ends anywhere (abandoned drag)', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n] } })
    const row = w.findAll('tbody tr')[0]
    const dataTransfer = { types: [DRAG_MIME], getData: () => '', dropEffect: '' }
    await row.trigger('dragover', { dataTransfer })
    expect(row.attributes('data-dropping')).toBe('true')
    document.dispatchEvent(new Event('dragend'))
    await nextTick()
    expect(row.attributes('data-dropping')).toBeUndefined()
  })
})
