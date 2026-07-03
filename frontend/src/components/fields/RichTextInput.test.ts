import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import RichTextInput from './RichTextInput.vue'

describe('RichTextInput', () => {
  it('renders initial HTML content', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>hello</p>' } })
    await flushPromises()
    expect(w.get('.rich-text__content').html()).toContain('hello')
  })

  it('emits update:modelValue as HTML when content changes', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' } })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { commands: { setContent: (h: string) => void } } }
    vm.editor.commands.setContent('<p>b</p>')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    expect(emitted).toBeTruthy()
    expect(String(emitted!.at(-1)![0])).toContain('b')
  })

  it('is not editable when disabled', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>', disabled: true } })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { isEditable: boolean } }
    expect(vm.editor.isEditable).toBe(false)
  })

  it('toggles bold via the toolbar', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' } })
    await flushPromises()
    const btn = w.get('[data-cmd="bold"]')
    await btn.trigger('click')
    const vm = w.vm as unknown as { editor: { isActive: (n: string) => boolean } }
    expect(vm.editor.isActive('bold')).toBe(true)
  })
})
