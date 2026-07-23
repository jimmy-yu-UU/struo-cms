import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import RichTextInput from './RichTextInput.vue'
import { fileContentPath } from '../../lib/richTextImages'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: {
    searchFiles: 'Search files…', loadFilesFailed: 'Failed to load files.',
    richtext: {
      bold: 'Bold', italic: 'Italic', strikethrough: 'Strikethrough',
      alignLeft: 'Align left', alignCenter: 'Align center', alignRight: 'Align right', alignJustify: 'Justify',
      heading2: 'Heading 2', heading3: 'Heading 3',
      subscript: 'Subscript', superscript: 'Superscript',
      bulletList: 'Bullet list', numberedList: 'Numbered list',
      blockquote: 'Blockquote', codeBlock: 'Code block',
      link: 'Link', horizontalRule: 'Horizontal rule', insertImage: 'Insert image',
      undo: 'Undo', redo: 'Redo',
      linkPrompt: 'Link URL', insertImageTitle: 'Insert image',
    },
  } } },
})

const stubs = { Dialog: true, Button: true, InputText: true, MediaGrid: true }

describe('RichTextInput', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('rejects a javascript: URL from the link prompt (defense-in-depth)', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { chain: () => unknown } }
    vi.spyOn(window, 'prompt').mockReturnValue('javascript:alert(1)')
    const chainSpy = vi.spyOn(vm.editor, 'chain')
    await w.get('[data-cmd="link"]').trigger('click')
    // The guard returns before any editor command runs, so no chain is built.
    expect(chainSpy).not.toHaveBeenCalled()
  })

  it('applies an https: URL from the link prompt', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { chain: () => unknown } }
    vi.spyOn(window, 'prompt').mockReturnValue('https://example.com')
    const chainSpy = vi.spyOn(vm.editor, 'chain')
    await w.get('[data-cmd="link"]').trigger('click')
    expect(chainSpy).toHaveBeenCalled()
  })

  it('renders initial HTML content', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>hello</p>' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    expect(w.get('.rich-text__content').html()).toContain('hello')
  })

  it('emits update:modelValue as HTML when content changes', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { commands: { setContent: (h: string) => void } } }
    vm.editor.commands.setContent('<p>b</p>')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    expect(emitted).toBeTruthy()
    expect(String(emitted!.at(-1)![0])).toContain('b')
  })

  it('is not editable when disabled', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>', disabled: true }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { isEditable: boolean } }
    expect(vm.editor.isEditable).toBe(false)
  })

  it('toggles bold via the toolbar', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    const btn = w.get('[data-cmd="bold"]')
    await btn.trigger('click')
    const vm = w.vm as unknown as { editor: { isActive: (n: string) => boolean } }
    expect(vm.editor.isActive('bold')).toBe(true)
  })

  it('inserts a managed image with relative src + data-file-id', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p>a</p>' },
      global: { plugins: [i18n], stubs: { Dialog: true, Button: true, MediaGrid: true } },
    })
    await flushPromises()
    const vm = w.vm as unknown as { insertImage: (id: string, alt?: string) => void }
    vm.insertImage('abc', 'cat')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    const html = String(emitted!.at(-1)![0])
    expect(html).toContain('data-file-id="abc"')
    expect(html).toContain(`src="${fileContentPath('abc')}"`)
  })

  it('sets text alignment via the toolbar', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    await w.get('[data-cmd="alignCenter"]').trigger('click')
    const vm = w.vm as unknown as { editor: { isActive: (a: Record<string, string>) => boolean } }
    expect(vm.editor.isActive({ textAlign: 'center' })).toBe(true)
  })

  it('subscript and superscript are mutually exclusive', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    await w.get('[data-cmd="subscript"]').trigger('click')
    const vm = w.vm as unknown as { editor: { isActive: (n: string) => boolean } }
    expect(vm.editor.isActive('subscript')).toBe(true)
    await w.get('[data-cmd="superscript"]').trigger('click')
    expect(vm.editor.isActive('superscript')).toBe(true)
    expect(vm.editor.isActive('subscript')).toBe(false)
  })

  it('applies colour via the colour menu', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    const vm = w.vm as unknown as {
      editor: { commands: { selectAll: () => void }; getAttributes: (n: string) => Record<string, unknown> }
    }
    vm.editor.commands.selectAll()
    await w.get('[data-cmd="color"]').trigger('click')
    await w.get('[data-color="#dc2626"]').trigger('click')
    expect(vm.editor.getAttributes('textStyle').color).toBe('#dc2626')
  })

  it('inserts a 3x3 table with header row via the table menu', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: { plugins: [i18n], stubs } })
    await flushPromises()
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="tableInsert"]').trigger('click')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    const html = String(emitted!.at(-1)![0])
    expect(html).toContain('<table')
    expect(html).toContain('<th')
  })
})
