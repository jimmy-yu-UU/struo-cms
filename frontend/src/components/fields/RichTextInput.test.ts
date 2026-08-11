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

// Dialog/Popover are reka compound components: DialogContent/PopoverContent inject context that
// only the real DialogRoot/PopoverRoot provides, so those roots are mounted for real rather than
// object-stubbed. Their own portal wrapper is itself named Teleport, so it collides with VTU's
// teleport stub and drops slot content unless renderStubDefaultSlot is on; that in turn also
// renders the default slot of the object-stubbed leaf components below (Button, MediaGrid).
const stubs = { Button: true, MediaGrid: true, teleport: true }
const globalOpts = { plugins: [i18n], stubs, renderStubDefaultSlot: true }

describe('RichTextInput', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('rejects a javascript: URL from the link prompt (defense-in-depth)', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { chain: () => unknown } }
    vi.spyOn(window, 'prompt').mockReturnValue('javascript:alert(1)')
    const chainSpy = vi.spyOn(vm.editor, 'chain')
    await w.get('[data-cmd="link"]').trigger('click')
    // The guard returns before any editor command runs, so no chain is built.
    expect(chainSpy).not.toHaveBeenCalled()
  })

  it('applies an https: URL from the link prompt', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { chain: () => unknown } }
    vi.spyOn(window, 'prompt').mockReturnValue('https://example.com')
    const chainSpy = vi.spyOn(vm.editor, 'chain')
    await w.get('[data-cmd="link"]').trigger('click')
    expect(chainSpy).toHaveBeenCalled()
  })

  it('renders initial HTML content', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>hello</p>' }, global: globalOpts })
    await flushPromises()
    expect(w.get('.rich-text__content').html()).toContain('hello')
  })

  it('emits update:modelValue as HTML when content changes', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { commands: { setContent: (h: string) => void } } }
    vm.editor.commands.setContent('<p>b</p>')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    expect(emitted).toBeTruthy()
    expect(String(emitted!.at(-1)![0])).toContain('b')
  })

  it('is not editable when disabled', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>', disabled: true }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { isEditable: boolean } }
    expect(vm.editor.isEditable).toBe(false)
  })

  it('toggles bold via the toolbar', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    const btn = w.get('[data-cmd="bold"]')
    await btn.trigger('click')
    const vm = w.vm as unknown as { editor: { isActive: (n: string) => boolean } }
    expect(vm.editor.isActive('bold')).toBe(true)
  })

  it('inserts a managed image with relative src + data-file-id', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p>a</p>' },
      global: globalOpts,
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
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    await w.get('[data-cmd="alignCenter"]').trigger('click')
    const vm = w.vm as unknown as { editor: { isActive: (a: Record<string, string>) => boolean } }
    expect(vm.editor.isActive({ textAlign: 'center' })).toBe(true)
  })

  it('subscript and superscript are mutually exclusive', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    await w.get('[data-cmd="subscript"]').trigger('click')
    const vm = w.vm as unknown as { editor: { isActive: (n: string) => boolean } }
    expect(vm.editor.isActive('subscript')).toBe(true)
    await w.get('[data-cmd="superscript"]').trigger('click')
    expect(vm.editor.isActive('superscript')).toBe(true)
    expect(vm.editor.isActive('subscript')).toBe(false)
  })

  it('applies colour via the colour menu', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts })
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
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="tableInsert"]').trigger('click')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    const html = String(emitted!.at(-1)![0])
    expect(html).toContain('<table')
    expect(html).toContain('<th')
  })

  it('keeps every toolbar command reachable after the control swap', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    const commands = ['bold', 'italic', 'strike', 'alignLeft', 'alignCenter', 'alignRight',
      'alignJustify', 'h2', 'h3', 'subscript', 'superscript', 'bulletList', 'orderedList',
      'blockquote', 'codeBlock', 'link', 'hr', 'image', 'undo', 'redo']
    for (const cmd of commands) {
      expect(w.find(`[data-cmd="${cmd}"]`).exists(), cmd).toBe(true)
    }
  })

  // The active/inactive toggle now rides a data-* attribute rather than a class, so twMerge never
  // has to fight the vendored button's own class list for the same background utility.
  it('flags the active toolbar state through data-active rather than a class', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    const btn = w.get('[data-cmd="bold"]')
    expect(btn.attributes('data-active')).toBe('false')
    await btn.trigger('click')
    // tiptap/vue-3 debounces its reactive editor state across two animation frames (see its
    // useDebouncedRef), so the template doesn't re-render on the very next microtask tick.
    await new Promise<void>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve())))
    await flushPromises()
    expect(w.get('[data-cmd="bold"]').attributes('data-active')).toBe('true')
  })

  it('no longer renders PrimeIcons font classes', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    expect(w.html()).not.toMatch(/class="[^"]*\bpi\b/)
  })

  // Icon-only toolbar buttons need names: today the pi-* buttons have no text node at all, so a
  // screen reader announces "button" for four alignment controls in a row.
  it('names every icon-only toolbar button', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    for (const cmd of ['alignLeft', 'bulletList', 'orderedList']) {
      const btn = w.get(`[data-cmd="${cmd}"]`)
      expect(btn.attributes('aria-label') || btn.text(), cmd).toBeTruthy()
    }
  })
})
