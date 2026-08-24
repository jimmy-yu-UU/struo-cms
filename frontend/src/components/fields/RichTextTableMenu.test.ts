import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextTableMenu from './RichTextTableMenu.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { richtext: {
    table: 'Table', customSize: 'Custom size…',
    tableSize: '{cols} columns × {rows} rows',
  } } } },
})

// reka's own portal wrapper is itself named Teleport, so it collides with VTU's teleport stub and
// drops slot content unless renderStubDefaultSlot is on.
const opts = { global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } }

describe('RichTextTableMenu', () => {
  // Replaces the old "emits insert and closes" test: that test drove a dedicated
  // `[data-cmd="tableInsert"]` button and asserted `emitted('action')` equalled `[['insert']]` —
  // both the hook and the payload shape are gone now that the toolbar button opens a size-picker
  // grid instead. This test drives the grid itself (RichTextTableGrid is already unit-tested on
  // its own in RichTextTableGrid.test.ts) and asserts the richer `insert` payload the grid path
  // now emits, plus that the popover still closes afterwards.
  it('emits insert with the picked size and a header row, and closes', async () => {
    const w = mount(RichTextTableMenu, { ...opts })
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cell="3-3"]').trigger('click')
    expect(w.emitted('insert')).toEqual([[{ rows: 3, cols: 3, withHeaderRow: true }]])
    expect(w.find('[data-cell="3-3"]').exists()).toBe(false)
    w.unmount()
  })

  // Replaces "disables in-table actions when outside a table" and "emits in-table actions when
  // inside a table": both pinned an `inTable` prop and a set of in-table action buttons
  // (addRowAfter, deleteTable, …) that this task deliberately removes from this component — those
  // eight operations moved to the table's own right-click menu in Task 4. This component is now
  // insert-only, so its replacement coverage is the "custom size…" escape hatch instead.
  it('emits customSize and closes when the custom-size entry is picked', async () => {
    const w = mount(RichTextTableMenu, { ...opts })
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="tableCustomSize"]').trigger('click')
    expect(w.emitted('customSize')).toEqual([[]])
    expect(w.find('[data-cmd="tableCustomSize"]').exists()).toBe(false)
    w.unmount()
  })

  it('disables the trigger when disabled', () => {
    const w = mount(RichTextTableMenu, { props: { disabled: true }, ...opts })
    expect(w.get('[data-cmd="table"]').attributes('disabled')).toBeDefined()
  })
})
