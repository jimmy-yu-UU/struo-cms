import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'
import { createI18n } from 'vue-i18n'
import MediaFileList from './MediaFileList.vue'
import MediaContextMenu from './MediaContextMenu.vue'
import { DRAG_MIME, serializeMovePayload, type MovePayload } from '../../lib/mediaMove'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    colName: 'Name', colType: 'Type', colSize: 'Size', colDimensions: 'Dimensions', colUploaded: 'Uploaded',
    colTypeFolder: 'Folder', folderRename: 'Rename folder', folderDelete: 'Delete folder',
    menuOpen: 'Open', menuMove: 'Move to…', menuRename: 'Rename', menuDelete: 'Delete',
  } } },
})

const files = [
  { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1024, width: 800, height: 600, createdAt: '2026-07-01T00:00:00Z' },
  { id: 'f2', fileName: 'b.pdf', contentType: 'application/pdf', size: 2048 },
]

const folders = [{ id: 'd1', name: 'Docs', parentId: null }]

function mountList(props: Record<string, unknown> = {}, extra: Record<string, unknown> = {}) {
  return mount(MediaFileList, {
    props: { files, ...props },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
    ...extra,
  })
}

async function openRowMenu(w: ReturnType<typeof mountList>, index: number) {
  await w.findAll('tbody tr')[index].trigger('contextmenu')
  await flushPromises()
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
      global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    expect(w.findAll('.act')).toHaveLength(files.length)
  })

  it('renders folder rows ahead of file rows', () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
    const rows = w.findAll('tbody tr')
    expect(rows).toHaveLength(folders.length + files.length)
    expect(rows[0].text()).toContain('Docs')
    expect(rows[0].text()).toContain('Folder')
  })

  it('emits openFolder when a folder row name is activated', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
    await w.find('tbody tr .media-list__open').trigger('click')
    expect(w.emitted('openFolder')?.[0]).toEqual(['d1'])
  })

  it('renders no folder rows when none are passed', () => {
    const w = mount(MediaFileList, { props: { files }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
    expect(w.findAll('tbody tr')).toHaveLength(files.length)
  })

  it('emits dropOn when a media drag is dropped on a folder row', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
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
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
    const dataTransfer = {
      types: [DRAG_MIME],
      getData: () => serializeMovePayload({ files: ['f1'], folders: [] }),
      dropEffect: '',
    }
    await w.findAll('tbody tr')[folders.length].trigger('drop', { dataTransfer })
    expect(w.emitted('dropOn')).toBeUndefined()
  })

  it('ignores a drop carrying no media payload', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
    const dataTransfer = { types: ['text/plain'], getData: () => 'hello', dropEffect: '' }
    await w.findAll('tbody tr')[0].trigger('drop', { dataTransfer })
    expect(w.emitted('dropOn')).toBeUndefined()
  })

  // Permissions: folder moves require canWrite('mediafolder') -- without that grant the row must
  // not be a drag source at all, regardless of canManageFolders (which also covers delete-only
  // users who should still see rename/delete but not drag folders around).
  it('is not draggable when canMoveFolders is false or unset', () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
    expect(w.findAll('tbody tr')[0].attributes('draggable')).toBeUndefined()
  })

  it('is draggable and writes the folder payload on dragstart when canMoveFolders is true', async () => {
    const w = mount(MediaFileList, {
      props: { files, folders, canManageFolders: true, canMoveFolders: true },
      global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
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
      global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    const row = w.findAll('tbody tr')[0]
    const setData = vi.fn()
    await row.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
    expect(setData).not.toHaveBeenCalled()
  })

  // Permissions: file moves require canWrite('file') -- same reasoning, file rows.
  it('is not draggable when canMoveFiles is false or unset', () => {
    const w = mount(MediaFileList, { props: { files, folders }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
    expect(w.findAll('tbody tr')[folders.length].attributes('draggable')).toBeUndefined()
  })

  it('is draggable and writes the file payload on dragstart when canMoveFiles is true', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canMoveFiles: true }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
    const row = w.findAll('tbody tr')[folders.length]
    expect(row.attributes('draggable')).toBe('true')
    const setData = vi.fn()
    await row.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
    expect(setData).toHaveBeenCalledWith(DRAG_MIME, serializeMovePayload({ files: ['f1'], folders: [] }))
  })

  it('does not write a drag payload on dragstart when canMoveFiles is false (bubbled drag from the thumbnail)', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canMoveFiles: false }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
    const row = w.findAll('tbody tr')[folders.length]
    const setData = vi.fn()
    await row.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
    expect(setData).not.toHaveBeenCalled()
  })

  // dragleave follows the mouseout model (see MediaFolderCards): a row -> own-child crossing
  // targets the ROW itself with relatedTarget still inside it, and the highlight must survive
  // that; only a crossing whose relatedTarget has left the row entirely should clear it.
  it('keeps the drop-highlight when dragleave targets the row itself but relatedTarget is still inside it', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
    const row = w.findAll('tbody tr')[0]
    const dataTransfer = { types: [DRAG_MIME], getData: () => '', dropEffect: '' }
    await row.trigger('dragover', { dataTransfer })
    expect(row.attributes('data-dropping')).toBe('true')
    await row.trigger('dragleave', { relatedTarget: row.find('td').element })
    expect(row.attributes('data-dropping')).toBe('true')
  })

  it('clears the drop-highlight when dragleave bubbles from a child with relatedTarget outside the row', async () => {
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
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
    const w = mount(MediaFileList, { props: { files, folders, canManageFolders: true }, global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } })
    const row = w.findAll('tbody tr')[0]
    const dataTransfer = { types: [DRAG_MIME], getData: () => '', dropEffect: '' }
    await row.trigger('dragover', { dataTransfer })
    expect(row.attributes('data-dropping')).toBe('true')
    document.dispatchEvent(new Event('dragend'))
    await nextTick()
    expect(row.attributes('data-dropping')).toBeUndefined()
  })

  // Right-click context menu (Task 8): folder rows get Open/Rename/Move/Delete, file rows get
  // Open/Move/Delete, and trash mode suppresses the file-row menu entirely (mirrors MediaGrid).
  describe('context menu', () => {
    it('always offers Open on a file row, regardless of grants', async () => {
      const w = mountList({ folders })
      await openRowMenu(w, folders.length)
      expect(w.find('[data-test="menu-open"]').exists()).toBe(true)
    })

    it('hides Move/Delete on a file row without the matching grant', async () => {
      const w = mountList({ folders })
      await openRowMenu(w, folders.length)
      expect(w.find('[data-test="menu-move"]').exists()).toBe(false)
      expect(w.find('[data-test="menu-delete"]').exists()).toBe(false)
      expect(w.find('[data-test="menu-rename"]').exists()).toBe(false)
    })

    it('shows Move/Delete on a file row when canMoveFiles/canDeleteFiles are true', async () => {
      const w = mountList({ folders, canMoveFiles: true, canDeleteFiles: true })
      await openRowMenu(w, folders.length)
      expect(w.find('[data-test="menu-move"]').exists()).toBe(true)
      expect(w.find('[data-test="menu-delete"]').exists()).toBe(true)
    })

    it('emits open with the file id when Open is selected on a file row', async () => {
      const w = mountList({ folders })
      await openRowMenu(w, folders.length + 1)
      await w.find('[data-test="menu-open"]').trigger('click')
      expect(w.emitted('open')?.[0]).toEqual(['f2'])
    })

    it('emits requestMove with a single-file payload when Move is selected on a file row', async () => {
      const w = mountList({ folders, canMoveFiles: true })
      await openRowMenu(w, folders.length + 1)
      await w.find('[data-test="menu-move"]').trigger('click')
      expect(w.emitted('requestMove')?.[0]).toEqual([{ files: ['f2'], folders: [] }])
    })

    it('emits remove with the file id when Delete is selected on a file row', async () => {
      const w = mountList({ folders, canDeleteFiles: true })
      await openRowMenu(w, folders.length + 1)
      await w.find('[data-test="menu-delete"]').trigger('click')
      expect(w.emitted('remove')?.[0]).toEqual(['f2'])
    })

    it('does not open a file-row menu at all when trashMode is true', async () => {
      const w = mountList({ folders, canMoveFiles: true, canDeleteFiles: true, trashMode: true })
      await openRowMenu(w, folders.length)
      expect(w.find('[data-test="menu-open"]').exists()).toBe(false)
    })

    it('always offers Open on a folder row, regardless of grants', async () => {
      const w = mountList({ folders })
      await openRowMenu(w, 0)
      expect(w.find('[data-test="menu-open"]').exists()).toBe(true)
    })

    it('hides Rename/Move/Delete on a folder row without the matching grants', async () => {
      const w = mountList({ folders })
      await openRowMenu(w, 0)
      expect(w.find('[data-test="menu-rename"]').exists()).toBe(false)
      expect(w.find('[data-test="menu-move"]').exists()).toBe(false)
      expect(w.find('[data-test="menu-delete"]').exists()).toBe(false)
    })

    it('shows Rename/Move/Delete on a folder row when their grants are true', async () => {
      const w = mountList({ folders, canRenameFolders: true, canMoveFolders: true, canDeleteFolders: true })
      await openRowMenu(w, 0)
      expect(w.find('[data-test="menu-rename"]').exists()).toBe(true)
      expect(w.find('[data-test="menu-move"]').exists()).toBe(true)
      expect(w.find('[data-test="menu-delete"]').exists()).toBe(true)
    })

    it('emits openFolder with the folder id when Open is selected on a folder row', async () => {
      const w = mountList({ folders })
      await openRowMenu(w, 0)
      await w.find('[data-test="menu-open"]').trigger('click')
      expect(w.emitted('openFolder')?.[0]).toEqual(['d1'])
    })

    it('emits renameFolder with the folder object when Rename is selected on a folder row', async () => {
      const w = mountList({ folders, canRenameFolders: true })
      await openRowMenu(w, 0)
      await w.find('[data-test="menu-rename"]').trigger('click')
      expect(w.emitted('renameFolder')).toEqual([[folders[0]]])
    })

    it('emits requestMove with a single-folder payload when Move is selected on a folder row', async () => {
      const w = mountList({ folders, canMoveFolders: true })
      await openRowMenu(w, 0)
      await w.find('[data-test="menu-move"]').trigger('click')
      expect(w.emitted('requestMove')?.[0]).toEqual([{ files: [], folders: ['d1'] }])
    })

    it('emits removeFolder with the folder object when Delete is selected on a folder row', async () => {
      const w = mountList({ folders, canDeleteFolders: true })
      await openRowMenu(w, 0)
      await w.find('[data-test="menu-delete"]').trigger('click')
      expect(w.emitted('removeFolder')).toEqual([[folders[0]]])
    })

    // Same reasoning as MediaGrid/MediaFolderCards: reach the shared menu's own exposed handlers
    // directly, with the grants off, to prove the refusal is this component's own wiring.
    it('refuses to emit requestMove/remove via the file-row menu\'s own handlers when the grants are false', async () => {
      const w = mountList({ folders, canMoveFiles: false, canDeleteFiles: false })
      await openRowMenu(w, folders.length)
      const menu = w.findAllComponents(MediaContextMenu)[folders.length]
      const vm = menu.vm as unknown as { onMove: () => void; onRemove: () => void }
      vm.onMove(); vm.onRemove()
      expect(w.emitted('requestMove')).toBeUndefined()
      expect(w.emitted('remove')).toBeUndefined()
    })

    it('refuses to emit requestMove/renameFolder/removeFolder via the folder-row menu\'s own handlers when the grants are false', async () => {
      const w = mountList({ folders })
      await openRowMenu(w, 0)
      const menu = w.findAllComponents(MediaContextMenu)[0]
      const vm = menu.vm as unknown as { onMove: () => void; onRename: () => void; onRemove: () => void }
      vm.onMove(); vm.onRename(); vm.onRemove()
      expect(w.emitted('requestMove')).toBeUndefined()
      expect(w.emitted('renameFolder')).toBeUndefined()
      expect(w.emitted('removeFolder')).toBeUndefined()
    })

    // Fix for review finding 2: reka's ContextMenuTrigger also opens on a still touch/pen press,
    // armed by its OWN @pointerdown listener (reka-ui/src/ContextMenu/ContextMenuTrigger.vue:70-79,
    // bound at line 115) -- not a `contextmenu` event, so `@contextmenu.stop` alone never sees
    // that path. Without an equivalent `.stop` on pointerdown, a long press on a row would arm the
    // timer on the row's own trigger AND bubble to open the outer empty-space trigger too.
    //
    // A default `mount()` attaches to a detached fragment, not `document` -- a dispatched event
    // bubbles to the top of THAT fragment and stops there regardless of whether `.stop` is
    // present, so a `document`-level listener would never fire either way and the assertion
    // would be unfalsifiable. `attachTo: document.body` puts the mounted tree in the real
    // document so bubbling (or its absence) is actually observable.
    it('stops pointerdown from bubbling past a folder row', async () => {
      const w = mountList({ folders }, { attachTo: document.body })
      const spy = vi.fn()
      document.addEventListener('pointerdown', spy)
      try {
        await w.findAll('tbody tr')[0].trigger('pointerdown', { pointerType: 'touch' })
      } finally {
        document.removeEventListener('pointerdown', spy)
        w.unmount()
      }
      expect(spy).not.toHaveBeenCalled()
    })

    it('stops pointerdown from bubbling past a file row', async () => {
      const w = mountList({ folders }, { attachTo: document.body })
      const spy = vi.fn()
      document.addEventListener('pointerdown', spy)
      try {
        await w.findAll('tbody tr')[folders.length].trigger('pointerdown', { pointerType: 'touch' })
      } finally {
        document.removeEventListener('pointerdown', spy)
        w.unmount()
      }
      expect(spy).not.toHaveBeenCalled()
    })
  })

  // Task 9: batch selection, both row kinds.
  describe('batch selection', () => {
    const empty: MovePayload = { files: [], folders: [] }

    it('renders no checkbox while the selection is empty', () => {
      const w = mountList({ folders, canMoveFiles: true, canMoveFolders: true, selection: empty })
      expect(w.find('[role="checkbox"]').exists()).toBe(false)
    })

    it('shows a checkbox on every row once the selection is non-empty', () => {
      const w = mountList({ folders, canMoveFiles: true, canMoveFolders: true, selection: { files: ['f1'], folders: [] } })
      expect(w.findAll('[role="checkbox"]')).toHaveLength(folders.length + files.length)
    })

    it('checks the box for a selected file row and leaves an unselected one unchecked', () => {
      // No folders here so the checkbox indices map 1:1 onto files (f1, f2) with no ambiguity
      // about whether a folder-row checkbox also rendered ahead of them.
      const w = mountList({ folders: [], canMoveFiles: true, selection: { files: ['f2'], folders: [] } })
      const boxes = w.findAll('[role="checkbox"]')
      expect(boxes[0].attributes('aria-checked')).toBe('false')
      expect(boxes[1].attributes('aria-checked')).toBe('true')
    })

    it('checks the box for a selected folder row', () => {
      const w = mountList({ folders, canMoveFolders: true, selection: { files: [], folders: ['d1'] } })
      expect(w.findAll('[role="checkbox"]')[0].attributes('aria-checked')).toBe('true')
    })

    it('does not show a file-row checkbox when canMoveFiles is false, even with a non-empty selection', () => {
      const w = mountList({ files: [files[0]], folders: [], canMoveFiles: false, selection: { files: ['f1'], folders: [] } })
      expect(w.find('[role="checkbox"]').exists()).toBe(false)
    })

    it('does not show a folder-row checkbox when canMoveFolders is false, even with a non-empty selection', () => {
      const w = mountList({ files: [], folders, canMoveFolders: false, selection: { files: [], folders: ['d1'] } })
      expect(w.find('[role="checkbox"]').exists()).toBe(false)
    })

    it('emits toggleSelect(file, id) when a file checkbox is toggled, and does not also open', async () => {
      const w = mountList({ folders: [], canMoveFiles: true, selection: { files: ['f2'], folders: [] } })
      const boxes = w.findAll('[role="checkbox"]')
      await boxes[0].trigger('click')
      expect(w.emitted('toggleSelect')?.[0]).toEqual(['file', 'f1'])
      expect(w.emitted('open')).toBeUndefined()
    })

    it('emits toggleSelect(folder, id) when a folder checkbox is toggled, and does not also open', async () => {
      // A non-empty selection (an unrelated file) is what makes the checkbox render at all --
      // d1 itself stays unselected/unchecked here.
      const w = mountList({ files: [], folders, canMoveFolders: true, selection: { files: ['other'], folders: [] } })
      await w.findAll('[role="checkbox"]')[0].trigger('click')
      expect(w.emitted('toggleSelect')?.[0]).toEqual(['folder', 'd1'])
      expect(w.emitted('openFolder')).toBeUndefined()
    })

    it('a plain click still opens the file row, even while a selection exists', async () => {
      const w = mountList({ folders, canMoveFiles: true, selection: { files: ['f2'], folders: [] } })
      await w.findAll('tbody tr')[folders.length].trigger('click')
      expect(w.emitted('open')?.[0]).toEqual(['f1'])
      expect(w.emitted('toggleSelect')).toBeUndefined()
    })

    it('a plain click still opens the folder row, even while a selection exists', async () => {
      const w = mountList({ folders, canMoveFolders: true, selection: { files: ['f2'], folders: [] } })
      await w.findAll('tbody tr')[0].trigger('click')
      expect(w.emitted('openFolder')?.[0]).toEqual(['d1'])
      expect(w.emitted('toggleSelect')).toBeUndefined()
    })

    it('a ctrl-click on a file row toggles selection instead of opening', async () => {
      const w = mountList({ folders, canMoveFiles: true, selection: empty })
      await w.findAll('tbody tr')[folders.length].trigger('click', { ctrlKey: true })
      expect(w.emitted('toggleSelect')?.[0]).toEqual(['file', 'f1'])
      expect(w.emitted('open')).toBeUndefined()
    })

    it('a shift-click on a folder row toggles selection instead of opening', async () => {
      const w = mountList({ folders, canMoveFolders: true, selection: empty })
      await w.findAll('tbody tr')[0].trigger('click', { shiftKey: true })
      expect(w.emitted('toggleSelect')?.[0]).toEqual(['folder', 'd1'])
      expect(w.emitted('openFolder')).toBeUndefined()
    })

    // Same reasoning as MediaGrid/MediaFolderCards: reach the exposed handlers directly, with the
    // grants off, to prove the refusal is this component's own doing.
    it('refuses to emit toggleSelect for a file via its own handler when canMoveFiles is false', () => {
      const w = mountList({ folders, canMoveFiles: false, selection: empty })
      ;(w.vm as unknown as { toggleFileSelect: (id: string) => void }).toggleFileSelect('f1')
      expect(w.emitted('toggleSelect')).toBeUndefined()
    })

    it('refuses to emit toggleSelect for a folder via its own handler when canMoveFolders is false', () => {
      const w = mountList({ folders, canMoveFolders: false, selection: empty })
      ;(w.vm as unknown as { toggleFolderSelect: (id: string) => void }).toggleFolderSelect('d1')
      expect(w.emitted('toggleSelect')).toBeUndefined()
    })

    it('drags only the clicked file when it is not part of the current selection', async () => {
      const w = mountList({ folders, canMoveFiles: true, selection: { files: ['f2'], folders: [] } })
      const row = w.findAll('tbody tr')[folders.length]
      const setData = vi.fn()
      await row.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
      expect(setData).toHaveBeenCalledWith(DRAG_MIME, serializeMovePayload({ files: ['f1'], folders: [] }))
    })

    it('drags the whole selection when the dragged file is already selected', async () => {
      const selection = { files: ['f1', 'f2'], folders: ['d9'] }
      const w = mountList({ folders, canMoveFiles: true, selection })
      const row = w.findAll('tbody tr')[folders.length]
      const setData = vi.fn()
      await row.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
      expect(setData).toHaveBeenCalledWith(DRAG_MIME, serializeMovePayload(selection))
    })

    it('drags only the clicked folder when it is not part of the current selection', async () => {
      const w = mountList({ folders, canMoveFolders: true, selection: { files: [], folders: ['other'] } })
      const row = w.findAll('tbody tr')[0]
      const setData = vi.fn()
      await row.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
      expect(setData).toHaveBeenCalledWith(DRAG_MIME, serializeMovePayload({ files: [], folders: ['d1'] }))
    })

    it('drags the whole selection when the dragged folder is already selected', async () => {
      const selection = { files: ['f9'], folders: ['d1'] }
      const w = mountList({ folders, canMoveFolders: true, selection })
      const row = w.findAll('tbody tr')[0]
      const setData = vi.fn()
      await row.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
      expect(setData).toHaveBeenCalledWith(DRAG_MIME, serializeMovePayload(selection))
    })
  })
})
