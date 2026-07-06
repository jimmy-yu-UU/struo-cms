import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import RichTextTableMenu from './RichTextTableMenu.vue'

describe('RichTextTableMenu', () => {
  it('emits insert and closes', async () => {
    const w = mount(RichTextTableMenu, { props: { inTable: false } })
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="tableInsert"]').trigger('click')
    expect(w.emitted('action')).toEqual([['insert']])
    expect(w.find('.table-menu__panel').exists()).toBe(false)
  })

  it('disables in-table actions when outside a table', async () => {
    const w = mount(RichTextTableMenu, { props: { inTable: false } })
    await w.get('[data-cmd="table"]').trigger('click')
    expect(w.get('[data-cmd="table-deleteTable"]').attributes('disabled')).toBeDefined()
    expect(w.get('[data-cmd="tableInsert"]').attributes('disabled')).toBeUndefined()
  })

  it('emits in-table actions when inside a table', async () => {
    const w = mount(RichTextTableMenu, { props: { inTable: true } })
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="table-addRowAfter"]').trigger('click')
    expect(w.emitted('action')).toEqual([['addRowAfter']])
  })

  it('disables the trigger when disabled', () => {
    const w = mount(RichTextTableMenu, { props: { inTable: false, disabled: true } })
    expect(w.get('[data-cmd="table"]').attributes('disabled')).toBeDefined()
  })
})
