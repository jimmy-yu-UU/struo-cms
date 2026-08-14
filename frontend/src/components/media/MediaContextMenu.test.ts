import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaContextMenu from './MediaContextMenu.vue'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en: { media: {
    menuOpen: 'Open', menuMove: 'Move to…', menuRename: 'Rename', menuDelete: 'Delete',
  } } },
})

type MenuProps = InstanceType<typeof MediaContextMenu>['$props']

function mountMenu(props: MenuProps) {
  // reka's ContextMenu portal is itself named "Teleport" -- see UserMenu.test.ts for the same
  // stub requirement with DropdownMenu.
  return mount(MediaContextMenu, {
    props,
    slots: { default: '<button type="button" class="target">Item</button>' },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

async function openMenu(w: ReturnType<typeof mountMenu>) {
  await w.find('.target').trigger('contextmenu')
  await flushPromises()
}

describe('MediaContextMenu', () => {
  it('renders the slot content as the trigger element, not a wrapping element of its own', () => {
    const w = mountMenu({ kind: 'file' })
    expect(w.find('.target').exists()).toBe(true)
  })

  it('opens the menu on right-click and shows Open for a file with no move/delete grants', async () => {
    const w = mountMenu({ kind: 'file' })
    await openMenu(w)
    expect(w.find('[data-test="menu-open"]').exists()).toBe(true)
    expect(w.find('[data-test="menu-move"]').exists()).toBe(false)
    expect(w.find('[data-test="menu-delete"]').exists()).toBe(false)
    expect(w.find('[data-test="menu-rename"]').exists()).toBe(false)
  })

  it('shows Move and Delete for a file when both grants are true', async () => {
    const w = mountMenu({ kind: 'file', canMove: true, canDelete: true })
    await openMenu(w)
    expect(w.find('[data-test="menu-move"]').exists()).toBe(true)
    expect(w.find('[data-test="menu-delete"]').exists()).toBe(true)
  })

  it('never shows Rename for a file kind, even when canRename is true', async () => {
    const w = mountMenu({ kind: 'file', canRename: true })
    await openMenu(w)
    expect(w.find('[data-test="menu-rename"]').exists()).toBe(false)
  })

  it('shows Rename for a folder only when canRename is true', async () => {
    const withGrant = mountMenu({ kind: 'folder', canRename: true })
    await openMenu(withGrant)
    expect(withGrant.find('[data-test="menu-rename"]').exists()).toBe(true)

    const withoutGrant = mountMenu({ kind: 'folder', canRename: false })
    await openMenu(withoutGrant)
    expect(withoutGrant.find('[data-test="menu-rename"]').exists()).toBe(false)
  })

  it('emits open when Open is selected', async () => {
    const w = mountMenu({ kind: 'file' })
    await openMenu(w)
    await w.find('[data-test="menu-open"]').trigger('click')
    expect(w.emitted('open')).toHaveLength(1)
  })

  it('emits move when Move is selected', async () => {
    const w = mountMenu({ kind: 'file', canMove: true })
    await openMenu(w)
    await w.find('[data-test="menu-move"]').trigger('click')
    expect(w.emitted('move')).toHaveLength(1)
  })

  it('emits remove when Delete is selected', async () => {
    const w = mountMenu({ kind: 'folder', canDelete: true })
    await openMenu(w)
    await w.find('[data-test="menu-delete"]').trigger('click')
    expect(w.emitted('remove')).toHaveLength(1)
  })

  it('emits rename when Rename is selected on a folder', async () => {
    const w = mountMenu({ kind: 'folder', canRename: true })
    await openMenu(w)
    await w.find('[data-test="menu-rename"]').trigger('click')
    expect(w.emitted('rename')).toHaveLength(1)
  })

  // reka's own ContextMenuItem already refuses to emit `select` when `disabled` is set, and
  // @vue/test-utils' trigger('click') dispatches fine on it regardless (it is a <div
  // role="menuitem">, not a native <button>, so VTU's isDisabled() check never applies) -- so a
  // DOM-driven test alone would pass even if this component's own guard were deleted, as long as
  // reka's internal one stayed intact. Call the exposed handlers directly, the same way
  // MediaMoveDialog's test calls `choose()` on a disabled option, to prove THIS component's own
  // guard -- not reka's -- is what refuses the action.
  it('does not emit move from the exposed handler when canMove is false', () => {
    const w = mountMenu({ kind: 'file', canMove: false })
    ;(w.vm as unknown as { onMove: () => void }).onMove()
    expect(w.emitted('move')).toBeUndefined()
  })

  it('does not emit remove from the exposed handler when canDelete is false', () => {
    const w = mountMenu({ kind: 'file', canDelete: false })
    ;(w.vm as unknown as { onRemove: () => void }).onRemove()
    expect(w.emitted('remove')).toBeUndefined()
  })

  it('does not emit rename from the exposed handler when canRename is false', () => {
    const w = mountMenu({ kind: 'folder', canRename: false })
    ;(w.vm as unknown as { onRename: () => void }).onRename()
    expect(w.emitted('rename')).toBeUndefined()
  })

  it('forwards disabled to the trigger so a disabled menu never opens (e.g. trashed items)', async () => {
    const w = mountMenu({ kind: 'file', canMove: true, canDelete: true, disabled: true })
    await openMenu(w)
    expect(w.find('[data-test="menu-open"]').exists()).toBe(false)
  })
})
