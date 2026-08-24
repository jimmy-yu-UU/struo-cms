import { describe, it, expect, afterEach } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextTableContextMenu from './RichTextTableContextMenu.vue'
import { IN_TABLE_ACTIONS } from './richTextTableActions'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { richtext: {
    addRowBefore: 'Add row above', addRowAfter: 'Add row below',
    addColumnBefore: 'Add column left', addColumnAfter: 'Add column right',
    deleteRow: 'Delete row', deleteColumn: 'Delete column',
    toggleHeaderRow: 'Toggle header row', deleteTable: 'Delete table',
  } } } },
})

// reka's ContextMenu portal is itself named "Teleport", so it collides with VTU's teleport stub
// and drops its content unless renderStubDefaultSlot is on -- same reasoning as
// MediaContextMenu.test.ts.
function build(): VueWrapper {
  return mount(RichTextTableContextMenu, {
    props: {},
    slots: { default: '<div class="target">cell</div>' },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

let w: VueWrapper | null = null
afterEach(() => { w?.unmount(); w = null })

describe('RichTextTableContextMenu', () => {
  it('emits every in-table action', async () => {
    w = build()
    // reka closes the menu on `select` (observed: the second data-cmd query fails once the first
    // click has fired), so the menu has to be re-opened before every action rather than once
    // up front -- this drives the same interaction a real user repeats, it does not loosen the
    // assertion below.
    for (const action of IN_TABLE_ACTIONS) {
      await w.find('.target').trigger('contextmenu')
      await w.get(`[data-cmd="table-${action}"]`).trigger('click')
    }
    const emitted = (w.emitted('action') ?? []).map((e) => (e as [string])[0])
    expect(emitted).toEqual([...IN_TABLE_ACTIONS])
  })

  it('emits nothing while disabled, and the handler refuses too', async () => {
    w = mount(RichTextTableContextMenu, {
      props: { disabled: true },
      slots: { default: '<div class="target">cell</div>' },
      global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    await w.find('.target').trigger('contextmenu')
    expect(w.find('[data-cmd="table-deleteTable"]').exists()).toBe(false)
    // The handler is the actual guard, not the absent DOM -- call it directly with disabled on.
    ;(w.vm as unknown as { run: (a: string) => void }).run('deleteTable')
    expect(w.emitted('action')).toBeUndefined()
  })
})
