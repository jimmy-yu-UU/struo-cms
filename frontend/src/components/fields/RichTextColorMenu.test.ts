import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import RichTextColorMenu from './RichTextColorMenu.vue'

describe('RichTextColorMenu', () => {
  it('opens the panel and emits pick for a palette swatch', async () => {
    const w = mount(RichTextColorMenu)
    await w.get('[data-cmd="color"]').trigger('click')
    await w.get('[data-color="#dc2626"]').trigger('click')
    expect(w.emitted('pick')).toEqual([['#dc2626']])
    expect(w.find('.color-menu__panel').exists()).toBe(false) // closes after pick
  })

  it('emits pick from the free colour input', async () => {
    const w = mount(RichTextColorMenu)
    await w.get('[data-cmd="color"]').trigger('click')
    const input = w.get('[data-cmd="colorFree"]')
    await input.setValue('#123456')
    expect(w.emitted('pick')).toEqual([['#123456']])
  })

  it('emits clear', async () => {
    const w = mount(RichTextColorMenu)
    await w.get('[data-cmd="color"]').trigger('click')
    await w.get('[data-cmd="colorClear"]').trigger('click')
    expect(w.emitted('clear')).toHaveLength(1)
  })

  it('disables the trigger when disabled', () => {
    const w = mount(RichTextColorMenu, { props: { disabled: true } })
    expect(w.get('[data-cmd="color"]').attributes('disabled')).toBeDefined()
  })
})
