import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import RichTextColorMenu from './RichTextColorMenu.vue'
import { i18n } from '../../i18n'

// reka's own portal wrapper is itself named Teleport, so it collides with VTU's teleport stub and
// drops slot content unless renderStubDefaultSlot is on.
const opts = { global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } }

describe('RichTextColorMenu', () => {
  it('opens the panel and emits pick for a palette swatch', async () => {
    const w = mount(RichTextColorMenu, opts)
    await w.get('[data-cmd="color"]').trigger('click')
    await w.get('[data-color="#dc2626"]').trigger('click')
    expect(w.emitted('pick')).toEqual([['#dc2626']])
    expect(w.find('[data-color]').exists()).toBe(false) // closes after pick
  })

  it('emits pick from the free colour input', async () => {
    const w = mount(RichTextColorMenu, opts)
    await w.get('[data-cmd="color"]').trigger('click')
    const input = w.get('[data-cmd="colorFree"]')
    await input.setValue('#123456')
    expect(w.emitted('pick')).toEqual([['#123456']])
  })

  it('emits clear', async () => {
    const w = mount(RichTextColorMenu, opts)
    await w.get('[data-cmd="color"]').trigger('click')
    await w.get('[data-cmd="colorClear"]').trigger('click')
    expect(w.emitted('clear')).toHaveLength(1)
  })

  it('disables the trigger when disabled', () => {
    const w = mount(RichTextColorMenu, { props: { disabled: true }, ...opts })
    expect(w.get('[data-cmd="color"]').attributes('disabled')).toBeDefined()
  })

  it('labels the trigger and the clear button from the active locale', async () => {
    i18n.global.locale.value = 'zh-TW'
    try {
      const w = mount(RichTextColorMenu, opts)
      expect(w.get('[data-cmd="color"]').attributes('aria-label')).toBe('文字顏色')
      await w.get('[data-cmd="color"]').trigger('click')
      expect(w.get('[data-cmd="colorClear"]').text()).toBe('清除顏色')
      expect(w.get('[data-color="#dc2626"]').attributes('aria-label')).toBe('顏色 #dc2626')
      expect(w.text()).toContain('自訂…')
    } finally {
      i18n.global.locale.value = 'en'
    }
  })
})
