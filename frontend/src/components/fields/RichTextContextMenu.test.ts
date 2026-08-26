import { describe, it, expect, afterEach } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextContextMenu from './RichTextContextMenu.vue'
import { IN_TABLE_ACTIONS } from './richTextTableActions'
import { IMAGE_ACTIONS } from './richTextImageActions'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { richtext: {
    addRowBefore: 'Add row above', addRowAfter: 'Add row below',
    addColumnBefore: 'Add column left', addColumnAfter: 'Add column right',
    deleteRow: 'Delete row', deleteColumn: 'Delete column',
    toggleHeaderRow: 'Toggle header row', deleteTable: 'Delete table',
    editAlt: 'Edit alt text', deleteImage: 'Delete image',
  } } } },
})

// reka's ContextMenu portal is itself named "Teleport", so it collides with VTU's teleport stub
// and drops its content unless renderStubDefaultSlot is on -- same reasoning as
// MediaContextMenu.test.ts.
function build(target: 'table' | 'image' | null, disabled?: boolean): VueWrapper {
  return mount(RichTextContextMenu, {
    props: { target, ...(disabled === undefined ? {} : { disabled }) },
    slots: { default: '<div class="target">cell</div>' },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

let w: VueWrapper | null = null
afterEach(() => { w?.unmount(); w = null })

describe('RichTextContextMenu', () => {
  it('emits every in-table action', async () => {
    w = build('table')
    // reka closes the menu on `select` (observed: the second data-cmd query fails once the first
    // click has fired), so the menu has to be re-opened before every action rather than once
    // up front -- this drives the same interaction a real user repeats, it does not loosen the
    // assertion below.
    for (const action of IN_TABLE_ACTIONS) {
      await w.find('.target').trigger('contextmenu')
      await w.get(`[data-cmd="table-${action}"]`).trigger('click')
    }
    const emitted = (w.emitted('tableAction') ?? []).map((e) => (e as [string])[0])
    expect(emitted).toEqual([...IN_TABLE_ACTIONS])
  })

  it('renders exactly one separator, before the first destructive table action', async () => {
    w = build('table')
    await w.find('.target').trigger('contextmenu')
    // Observed by printing the rendered menu HTML: reka's ContextMenuSeparator renders as a plain
    // sibling `<div role="separator" data-slot="context-menu-separator">` among the
    // ContextMenuItem divs, not nested inside one, so a single flat selector over both element
    // kinds preserves their real DOM order and lets the separator's position be checked directly
    // against its neighbour rather than assumed from IN_TABLE_ACTIONS.
    const nodes = w.findAll('[data-cmd], [role="separator"]')
    const separatorIndexes = nodes
      .map((n, i) => (n.attributes('role') === 'separator' ? i : -1))
      .filter((i) => i !== -1)
    expect(separatorIndexes).toHaveLength(1)
    const next = nodes[separatorIndexes[0] + 1]
    expect(next.attributes('data-cmd')).toBe('table-deleteRow')
  })

  it('emits nothing while disabled, and runTable refuses too', async () => {
    w = build('table', true)
    await w.find('.target').trigger('contextmenu')
    expect(w.find('[data-cmd="table-deleteTable"]').exists()).toBe(false)
    // The handler is the actual guard, not the absent DOM -- call it directly with disabled on.
    ;(w.vm as unknown as { runTable: (a: string) => void }).runTable('deleteTable')
    expect(w.emitted('tableAction')).toBeUndefined()
  })

  it('renders the image actions and no table actions when target is "image"', async () => {
    w = build('image')
    await w.find('.target').trigger('contextmenu')
    for (const action of IMAGE_ACTIONS) {
      expect(w.find(`[data-cmd="image-${action}"]`).exists()).toBe(true)
    }
    for (const action of IN_TABLE_ACTIONS) {
      expect(w.find(`[data-cmd="table-${action}"]`).exists()).toBe(false)
    }
  })

  it('emits every image action', async () => {
    w = build('image')
    for (const action of IMAGE_ACTIONS) {
      await w.find('.target').trigger('contextmenu')
      await w.get(`[data-cmd="image-${action}"]`).trigger('click')
    }
    const emitted = (w.emitted('imageAction') ?? []).map((e) => (e as [string])[0])
    expect(emitted).toEqual([...IMAGE_ACTIONS])
  })

  it('renders exactly one separator, before the first destructive image action', async () => {
    w = build('image')
    await w.find('.target').trigger('contextmenu')
    const nodes = w.findAll('[data-cmd], [role="separator"]')
    const separatorIndexes = nodes
      .map((n, i) => (n.attributes('role') === 'separator' ? i : -1))
      .filter((i) => i !== -1)
    expect(separatorIndexes).toHaveLength(1)
    const next = nodes[separatorIndexes[0] + 1]
    expect(next.attributes('data-cmd')).toBe('image-deleteImage')
  })

  it('emits nothing while disabled, and runImage refuses too', async () => {
    w = build('image', true)
    await w.find('.target').trigger('contextmenu')
    expect(w.find('[data-cmd="image-deleteImage"]').exists()).toBe(false)
    // The handler is the actual guard, not the absent DOM -- call it directly with disabled on.
    ;(w.vm as unknown as { runImage: (a: string) => void }).runImage('deleteImage')
    expect(w.emitted('imageAction')).toBeUndefined()
  })

  it('renders no items at all when target is null', async () => {
    w = build(null)
    await w.find('.target').trigger('contextmenu')
    expect(w.findAll('[data-cmd]')).toHaveLength(0)
    expect(w.find('[role="separator"]').exists()).toBe(false)
  })
})
