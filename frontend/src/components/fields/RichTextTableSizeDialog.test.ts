import { describe, it, expect, afterEach } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextTableSizeDialog from './RichTextTableSizeDialog.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: {
    fields: { richtext: {
      customSizeTitle: 'Insert table', rows: 'Rows', columns: 'Columns',
      withHeaderRow: 'Include header row',
      sizeOutOfRange: 'Rows and columns must be between {min} and {max}.',
    } },
    // Real path is common.cancel / common.confirm, not top-level -- confirmed against
    // frontend/src/locales/en.ts before wiring the component to it.
    common: { cancel: 'Cancel', confirm: 'Insert' },
  } },
})

let w: VueWrapper | null = null
afterEach(() => { w?.unmount(); w = null })

function build(): VueWrapper {
  return mount(RichTextTableSizeDialog, {
    props: { open: true },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

describe('RichTextTableSizeDialog', () => {
  it('defaults to a 3x3 table with a header row', () => {
    w = build()
    const vm = w.vm as unknown as { rows: number; cols: number; withHeaderRow: boolean }
    expect(vm.rows).toBe(3)
    expect(vm.cols).toBe(3)
    expect(vm.withHeaderRow).toBe(true)
  })

  it('emits the chosen size', async () => {
    w = build()
    const vm = w.vm as unknown as { rows: number; cols: number; withHeaderRow: boolean; submit: () => void }
    vm.rows = 4
    vm.cols = 6
    vm.withHeaderRow = false
    vm.submit()
    expect(w.emitted('insert')).toEqual([[{ rows: 4, cols: 6, withHeaderRow: false }]])
  })

  it('refuses a size outside the shared bounds', async () => {
    w = build()
    const vm = w.vm as unknown as { rows: number; cols: number; valid: boolean; submit: () => void }
    vm.cols = 21
    await w.vm.$nextTick()
    expect(vm.valid).toBe(false)
    vm.submit()
    expect(w.emitted('insert')).toBeUndefined()
    vm.cols = 0
    await w.vm.$nextTick()
    expect(vm.valid).toBe(false)
  })

  // build() always mounts with open: true, which the reset watch (not `immediate`) cannot cover
  // -- only a true->false->true transition on an already-mounted instance exercises it. Without
  // this, a rejected attempt (rows/withHeaderRow left non-default) would still be showing the
  // next time the dialog opens.
  it('resets to the default when reopened', async () => {
    w = build()
    const vm = w.vm as unknown as { rows: number; withHeaderRow: boolean }
    vm.rows = 9
    vm.withHeaderRow = false
    await w.setProps({ open: false })
    await w.setProps({ open: true })
    expect(vm.rows).toBe(3)
    expect(vm.withHeaderRow).toBe(true)
  })

  // The other tests all drive the exposed refs directly, which never exercises the actual
  // v-model.number coercion on the real <input type="number"> -- a string reaching insertTable
  // downstream would not be caught by any of them.
  it('coerces the typed rows value to a number', async () => {
    w = build()
    const vm = w.vm as unknown as { rows: number }
    await w.get('[data-testid="rows"]').setValue('7')
    expect(typeof vm.rows).toBe('number')
    expect(vm.rows).toBe(7)
  })

  // A shared `valid` driving both inputs' aria-invalid would mark the rows field invalid too
  // merely because the columns field is out of range -- rows itself is still a perfectly fine
  // value and must not be flagged. Only the offending field's own aria-invalid should flip; the
  // error message stays the single shared paragraph (asserted separately below), so both inputs
  // keep pointing at it via aria-describedby regardless of which one is actually invalid.
  it('flags aria-invalid on only the out-of-range field, not both', async () => {
    w = build()
    const vm = w.vm as unknown as { cols: number }
    vm.cols = 21
    await w.vm.$nextTick()
    expect(w.get('[data-testid="cols"]').attributes('aria-invalid')).toBe('true')
    expect(w.get('[data-testid="rows"]').attributes('aria-invalid')).toBe('false')
    // Both still point at the same shared error message while it is showing.
    const errorId = w.get('[role="alert"]').attributes('id')
    expect(w.get('[data-testid="rows"]').attributes('aria-describedby')).toBe(errorId)
    expect(w.get('[data-testid="cols"]').attributes('aria-describedby')).toBe(errorId)
  })
})
