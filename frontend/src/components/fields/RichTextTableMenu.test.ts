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
  // Replaces the old "emits insert and closes" test: that test drove a dedicated
  // `[data-cmd="tableInsert"]` button and asserted `emitted('action')` equalled `[['insert']]` —
  // both the hook and the payload shape are gone now that the toolbar button opens a size-picker
  // grid instead. This test drives the grid itself (RichTextTableGrid is already unit-tested on
  // its own in RichTextTableGrid.test.ts) and asserts the richer `insert` payload the grid path
  // now emits, plus that the popover still closes afterwards. A non-square pick (2 rows, 4
  // cols), not 3x3: a rows/cols swap on the way into the emitted object would be invisible on a
  // square pick.
  it('emits insert with the picked size and a header row, and closes', async () => {
    const w = mount(RichTextTableMenu, { ...opts })
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cell="2-4"]').trigger('click')
    expect(w.emitted('insert')).toEqual([[{ rows: 2, cols: 4, withHeaderRow: true }]])
    expect(w.find('[data-cell="2-4"]').exists()).toBe(false)
    w.unmount()
  })

  // Replaces "disables in-table actions when outside a table" and "emits in-table actions when
  // inside a table": both pinned an `inTable` prop and a set of in-table action buttons
  // (addRowAfter, deleteTable, …) that this task deliberately removes from this component — those
  // eight operations moved to the table's own right-click menu in Task 4. This component is now
  // insert-only, so its replacement coverage is the "custom size…" escape hatch instead.
  // The payload is the trigger button itself, not a formality: it is the only way RichTextInput
  // can restore focus after the size dialog is cancelled, because this component deliberately
  // suppresses the popover's own close-auto-focus on this path and the entry that had focus
  // unmounts with the popover. Asserting the exact element, not just that something was emitted.
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

  // Review-round fix: verified this session in the installed reka-ui@2.10.3 source
  // (Popover/PopoverContentNonModal.js) that this popover's own onCloseAutoFocus handler, unless
  // preventDefault()'d, refocuses `[data-cmd="table"]` -- which RichTextInput.vue's own
  // openTableSizeDialog has, by the time this fires for real, already both blurred away from and
  // opened its OWN aria-hidden dialog over. The real close-auto-focus event PopoverContentNonModal
  // dispatches is itself timer-deferred (FocusScope's own cleanup schedules it via
  // `setTimeout(..., 0)`, a macrotask), so simulating it via $emit synchronously, right after the
  // click and before awaiting that click's own settle promise, is what tests
  // onPopoverCloseAutoFocus's own guard rather than racing the real one -- confirmed necessary by
  // first awaiting the click before emitting, which let the real deferred event already consume
  // (and correctly clear) the flag before this test's own simulated one ever ran, making the
  // assertion pass for the wrong reason.
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

  // The other half: the grid-pick path must NOT suppress this popover's own close-auto-focus --
  // restoring focus to `[data-cmd="table"]` there is the correct, wanted behaviour (no dialog
  // opens afterward to fight it over), so this must stay reka's own default, unprevented.
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
