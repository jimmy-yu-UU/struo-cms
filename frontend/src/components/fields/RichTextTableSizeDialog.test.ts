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
})
