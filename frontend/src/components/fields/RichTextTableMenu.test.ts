import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextTableMenu from './RichTextTableMenu.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { richtext: {
    table: 'Table',
    addRowBefore: 'Add row above', addRowAfter: 'Add row below',
    addColumnBefore: 'Add column left', addColumnAfter: 'Add column right',
    deleteRow: 'Delete row', deleteColumn: 'Delete column',
    toggleHeaderRow: 'Toggle header row', deleteTable: 'Delete table',
  } } } },
})

// reka's own portal wrapper is itself named Teleport, so it collides with VTU's teleport stub and
// drops slot content unless renderStubDefaultSlot is on.
const opts = { global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } }

describe('RichTextTableMenu', () => {
  it('emits insert and closes', async () => {
    const w = mount(RichTextTableMenu, { props: { inTable: false }, ...opts })
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="tableInsert"]').trigger('click')
    expect(w.emitted('action')).toEqual([['insert']])
    expect(w.find('[data-cmd="tableInsert"]').exists()).toBe(false)
  })

  it('disables in-table actions when outside a table', async () => {
    const w = mount(RichTextTableMenu, { props: { inTable: false }, ...opts })
    await w.get('[data-cmd="table"]').trigger('click')
    expect(w.get('[data-cmd="table-deleteTable"]').attributes('disabled')).toBeDefined()
    expect(w.get('[data-cmd="tableInsert"]').attributes('disabled')).toBeUndefined()
  })

  it('emits in-table actions when inside a table', async () => {
    const w = mount(RichTextTableMenu, { props: { inTable: true }, ...opts })
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="table-addRowAfter"]').trigger('click')
    expect(w.emitted('action')).toEqual([['addRowAfter']])
  })

  it('disables the trigger when disabled', () => {
    const w = mount(RichTextTableMenu, { props: { inTable: false, disabled: true }, ...opts })
    expect(w.get('[data-cmd="table"]').attributes('disabled')).toBeDefined()
  })
})
