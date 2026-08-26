import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextTableMenu from './RichTextTableMenu.vue'
import { PopoverContent } from '@/components/ui/popover'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { richtext: {
    table: 'Table', customSize: 'Custom size…',
    tableSizeCols: '{count} column | {count} columns', tableSizeRows: '{count} row | {count} rows',
  } } } },
})

// reka's own portal wrapper is itself named Teleport, so it collides with VTU's teleport stub and
// drops slot content unless renderStubDefaultSlot is on.
const opts = { global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } }

describe('RichTextTableMenu', () => {
  // A non-square pick (2 rows, 4 cols), not 3x3: a rows-cols swap on the way into the emitted
  // object would be invisible on a square pick.
  it('emits insert with the picked size and a header row, and closes', async () => {
    const w = mount(RichTextTableMenu, { ...opts })
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cell="2-4"]').trigger('click')
    expect(w.emitted('insert')).toEqual([[{ rows: 2, cols: 4, withHeaderRow: true }]])
    expect(w.find('[data-cell="2-4"]').exists()).toBe(false)
    w.unmount()
  })

  // Asserts the exact element in the payload, not just that something was emitted: that button is
  // the only focus target RichTextInput has left after the size dialog is cancelled.
  it('emits customSize with its own trigger button and closes when the custom-size entry is picked', async () => {
    const w = mount(RichTextTableMenu, { ...opts })
    const trigger = w.get('[data-cmd="table"]').element
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="tableCustomSize"]').trigger('click')
    expect(w.emitted('customSize')).toEqual([[trigger]])
    expect(w.find('[data-cmd="tableCustomSize"]').exists()).toBe(false)
    w.unmount()
  })

  it('disables the trigger when disabled', () => {
    const w = mount(RichTextTableMenu, { props: { disabled: true }, ...opts })
    expect(w.get('[data-cmd="table"]').attributes('disabled')).toBeDefined()
    w.unmount()
  })

  // The event is emitted synchronously, before the click's settle promise is awaited, and that
  // ordering is load-bearing: reka's real close-auto-focus event is deferred to a macrotask, so
  // awaiting the click first lets the real event consume and clear the flag, and this assertion
  // then passes for the wrong reason.
  it('prevents the popover default close-auto-focus after picking custom size', async () => {
    const w = mount(RichTextTableMenu, { ...opts })
    await w.get('[data-cmd="table"]').trigger('click')
    const clicked = w.get('[data-cmd="tableCustomSize"]').trigger('click')
    const event = new Event('closeAutoFocus', { cancelable: true })
    w.findComponent(PopoverContent).vm.$emit('closeAutoFocus', event)
    expect(event.defaultPrevented).toBe(true)
    await clicked
    w.unmount()
  })

  // The other half: the grid-pick path must NOT suppress the close-auto-focus -- no dialog opens
  // afterward, so restoring focus to the trigger is the wanted behaviour there.
  it('does not prevent the popover default close-auto-focus after picking a grid size', async () => {
    const w = mount(RichTextTableMenu, { ...opts })
    await w.get('[data-cmd="table"]').trigger('click')
    const picked = w.get('[data-cell="2-4"]').trigger('click')
    const event = new Event('closeAutoFocus', { cancelable: true })
    w.findComponent(PopoverContent).vm.$emit('closeAutoFocus', event)
    expect(event.defaultPrevented).toBe(false)
    await picked
    w.unmount()
  })
})
