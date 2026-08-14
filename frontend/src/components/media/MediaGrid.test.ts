import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaGrid from './MediaGrid.vue'
import MediaContextMenu from './MediaContextMenu.vue'
import { DRAG_MIME, serializeMovePayload } from '../../lib/mediaMove'

// MediaGrid now renders MediaContextMenu per tile, which calls useI18n() unconditionally in
// setup() -- every mount needs the i18n plugin from here on, not just the context-menu tests.
const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    menuOpen: 'Open', menuMove: 'Move to…', menuRename: 'Rename', menuDelete: 'Delete',
  } } },
})

const files = [
  { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 },
  { id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2 },
]

function mountGrid(props: Record<string, unknown> = {}, extra: Record<string, unknown> = {}) {
  return mount(MediaGrid, {
    props: { files, ...props },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
    ...extra,
  })
}

async function openTileMenu(w: ReturnType<typeof mountGrid>, index = 0) {
  await w.findAll('.media-tile')[index].trigger('contextmenu')
  await flushPromises()
}

describe('MediaGrid', () => {
  it('renders one tile per file', () => {
    const w = mountGrid()
    expect(w.findAll('.media-tile')).toHaveLength(2)
  })

  it('emits select with the file id on click when selectable', async () => {
    const w = mountGrid({ selectable: true })
    await w.findAll('.media-tile')[1].trigger('click')
    expect(w.emitted('select')?.[0]).toEqual(['f2'])
  })

  it('marks the selected tile', () => {
    const w = mountGrid({ selectable: true, selectedId: 'f2' })
    expect(w.findAll('.media-tile')[1].classes()).toContain('is-selected')
  })

  it('emits toggle with the id on click in multiple mode', async () => {
    const w = mountGrid({ multiple: true, selectedIds: [] })
    await w.findAll('.media-tile')[0].trigger('click')
    expect(w.emitted('toggle')?.[0]).toEqual(['f1'])
    expect(w.emitted('select')).toBeUndefined()
  })

  it('marks tiles whose id is in selectedIds (multiple mode)', () => {
    const w = mountGrid({ multiple: true, selectedIds: ['f2'] })
    expect(w.findAll('.media-tile')[1].classes()).toContain('is-selected')
    expect(w.findAll('.media-tile')[0].classes()).not.toContain('is-selected')
  })

  it('emits open with the id on click when not selectable', async () => {
    const w = mountGrid()
    await w.findAll('.media-tile')[0].trigger('click')
    expect(w.emitted('open')?.[0]).toEqual(['f1'])
    expect(w.emitted('select')).toBeUndefined()
    expect(w.emitted('toggle')).toBeUndefined()
  })

  it('renders the actions slot once per file', () => {
    const w = mountGrid({}, { slots: { actions: '<button class="act">{{ params.file.id }}</button>' } })
    expect(w.findAll('.act')).toHaveLength(files.length)
  })

  // Permissions: file moves require canWrite('file') -- without that grant a tile must not be a
  // drag source at all.
  it('is not draggable when canMove is false or unset', () => {
    const w = mountGrid()
    expect(w.find('.media-tile-wrap').attributes('draggable')).toBeUndefined()
  })

  it('is draggable and writes the file payload on dragstart when canMove is true', async () => {
    const w = mountGrid({ canMove: true })
    const wrap = w.find('.media-tile-wrap')
    expect(wrap.attributes('draggable')).toBe('true')
    const setData = vi.fn()
    await wrap.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
    expect(setData).toHaveBeenCalledWith(DRAG_MIME, serializeMovePayload({ files: ['f1'], folders: [] }))
  })

  // The wrapper's own `draggable` attribute is not the only way a `dragstart` can reach this
  // handler -- a child <img> is draggable by default in every browser and `dragstart` bubbles, so
  // gating only the attribute leaves the permission bypassable. `onDragStart` itself must refuse
  // to write a payload when canMove is false, regardless of what fired the event.
  it('does not write a drag payload on dragstart when canMove is false (bubbled drag from a child element)', async () => {
    const w = mountGrid({ canMove: false })
    const wrap = w.find('.media-tile-wrap')
    const setData = vi.fn()
    await wrap.trigger('dragstart', { dataTransfer: { setData, types: [], effectAllowed: '' } })
    expect(setData).not.toHaveBeenCalled()
  })

  // Right-click context menu (Task 8): Open is unconditional, Move/Delete follow their own grants.
  describe('context menu', () => {
    it('always offers Open, regardless of grants', async () => {
      const w = mountGrid({})
      await openTileMenu(w)
      expect(w.find('[data-test="menu-open"]').exists()).toBe(true)
    })

    it('hides Move and Delete without the matching grant', async () => {
      const w = mountGrid({})
      await openTileMenu(w)
      expect(w.find('[data-test="menu-move"]').exists()).toBe(false)
      expect(w.find('[data-test="menu-delete"]').exists()).toBe(false)
    })

    it('shows Move and Delete when canMove/canDelete are true', async () => {
      const w = mountGrid({ canMove: true, canDelete: true })
      await openTileMenu(w)
      expect(w.find('[data-test="menu-move"]').exists()).toBe(true)
      expect(w.find('[data-test="menu-delete"]').exists()).toBe(true)
    })

    it('never offers Rename (files have no rename entry)', async () => {
      const w = mountGrid({ canMove: true, canDelete: true })
      await openTileMenu(w)
      expect(w.find('[data-test="menu-rename"]').exists()).toBe(false)
    })

    it('emits open with the tile id when Open is selected', async () => {
      const w = mountGrid({})
      await openTileMenu(w, 1)
      await w.find('[data-test="menu-open"]').trigger('click')
      expect(w.emitted('open')?.[0]).toEqual(['f2'])
    })

    it('emits requestMove with a single-file payload when Move is selected', async () => {
      const w = mountGrid({ canMove: true })
      await openTileMenu(w, 1)
      await w.find('[data-test="menu-move"]').trigger('click')
      expect(w.emitted('requestMove')?.[0]).toEqual([{ files: ['f2'], folders: [] }])
    })

    it('emits remove with the tile id when Delete is selected', async () => {
      const w = mountGrid({ canDelete: true })
      await openTileMenu(w, 1)
      await w.find('[data-test="menu-delete"]').trigger('click')
      expect(w.emitted('remove')?.[0]).toEqual(['f2'])
    })

    // Trash tiles already have restore/purge as slot actions -- the context menu must not offer a
    // second, redundant path (and must not let Open reach a trashed item at all).
    it('does not open a menu at all when trashMode is true', async () => {
      const w = mountGrid({ canMove: true, canDelete: true, trashMode: true })
      await openTileMenu(w)
      expect(w.find('[data-test="menu-open"]').exists()).toBe(false)
    })

    // Same reasoning as MediaMoveDialog's own guard test: reka's ContextMenuItem already refuses
    // to fire `select` when `disabled`, and VTU's trigger('click') would dispatch fine regardless
    // (it's a <div role="menuitem">, not a native disabled <button>) -- neither of those proves
    // THIS component refuses. Reach the shared menu's own exposed handler directly, with the grant
    // off, to prove the refusal doesn't depend on the entry being hidden.
    it('refuses to emit remove/requestMove via the menu\'s own handler when the grants are false', () => {
      const w = mountGrid({ canMove: false, canDelete: false })
      const menu = w.findComponent(MediaContextMenu)
      ;(menu.vm as unknown as { onMove: () => void; onRemove: () => void }).onMove()
      ;(menu.vm as unknown as { onMove: () => void; onRemove: () => void }).onRemove()
      expect(w.emitted('requestMove')).toBeUndefined()
      expect(w.emitted('remove')).toBeUndefined()
    })

    // Fix for review finding 1: MediaGrid has three other callers besides MediaLibraryView --
    // FilePicker (selectable), FilesField (multiple) and RichTextInput (selectable) -- none of
    // which listens for `open`, and a menu there would both do nothing on select AND eat the
    // native browser context menu (reka's trigger calls preventDefault as soon as it opens).
    it('renders no context-menu trigger at all when selectable is set (picker mode)', async () => {
      const w = mountGrid({ selectable: true })
      expect(w.findComponent(MediaContextMenu).exists()).toBe(false)
      // The plain (unwrapped) tile must still exist and still be clickable -- gating the menu
      // must not regress the picker's own select behaviour.
      await w.findAll('.media-tile')[0].trigger('contextmenu')
      await flushPromises()
      expect(w.find('[data-test="menu-open"]').exists()).toBe(false)
      await w.findAll('.media-tile')[0].trigger('click')
      expect(w.emitted('select')?.[0]).toEqual(['f1'])
    })

    it('renders no context-menu trigger at all when multiple is set (picker mode)', async () => {
      const w = mountGrid({ multiple: true, selectedIds: [] })
      expect(w.findComponent(MediaContextMenu).exists()).toBe(false)
      await w.findAll('.media-tile')[0].trigger('click')
      expect(w.emitted('toggle')?.[0]).toEqual(['f1'])
    })

    // Fix for review finding 2: reka's ContextMenuTrigger also opens on a still touch/pen press,
    // armed by its OWN @pointerdown listener (reka-ui/src/ContextMenu/ContextMenuTrigger.vue:70-79,
    // bound at line 115) -- not a `contextmenu` event, so `@contextmenu.stop` alone never sees
    // that path. Without an equivalent `.stop` on pointerdown, a long press on a tile would arm
    // the timer on the tile's own trigger AND bubble to open the outer empty-space trigger too.
    //
    // A default `mount()` attaches to a detached fragment, not `document` -- a dispatched event
    // bubbles to the top of THAT fragment and stops there regardless of whether `.stop` is
    // present, so a `document`-level listener would never fire either way and the assertion
    // would be unfalsifiable. `attachTo: document.body` puts the mounted tree in the real
    // document so bubbling (or its absence) is actually observable.
    it('stops pointerdown from bubbling past the tile (reka\'s long-press path listens on pointerdown, not contextmenu)', async () => {
      const w = mountGrid({}, { attachTo: document.body })
      const spy = vi.fn()
      document.addEventListener('pointerdown', spy)
      try {
        await w.findAll('.media-tile')[0].trigger('pointerdown', { pointerType: 'touch' })
      } finally {
        document.removeEventListener('pointerdown', spy)
        w.unmount()
      }
      expect(spy).not.toHaveBeenCalled()
    })
  })
})
