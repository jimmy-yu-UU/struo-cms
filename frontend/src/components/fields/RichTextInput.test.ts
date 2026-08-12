import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import RichTextInput from './RichTextInput.vue'
import { fileContentPath } from '../../lib/richTextImages'
import { buttonVariants } from '@/components/ui/button'
import { cn } from '@/lib/utils'

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

  // tiptap/vue-3 debounces its reactive editor state across two animation frames (see its
  // useDebouncedRef in @tiptap/vue-3's Editor class), so a template re-render driven by
  // editor.isActive() needs that wait, unlike reading vm.editor.isActive() directly.
  async function waitForEditorReactivity(): Promise<void> {
    await new Promise<void>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve())))
    await flushPromises()
  }

  it('flags active toolbar state via data-active on a hand-written control', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    expect(w.get('[data-cmd="bold"]').attributes('data-active')).toBe('false')
    await w.get('[data-cmd="bold"]').trigger('click')
    await waitForEditorReactivity()
    expect(w.get('[data-cmd="bold"]').attributes('data-active')).toBe('true')
  })

  // alignCenter is one instance of the align v-for; a refactor that drops the data-active binding
  // from the loop (rather than from a single hand-written button) would only be caught by
  // asserting a templated control, not just hand-written ones. A fresh mount (rather than
  // chaining onto the bold click above) sidesteps tiptap's stored-mark semantics: toggling bold
  // with no text selected only stores it as a pending mark for the next typed character, and a
  // later, unrelated command clears that pending mark — real editor behaviour, not something this
  // migration changed, but it would make a combined assertion flaky for reasons unrelated to
  // data-active.
  it('flags active toolbar state via data-active on a templated control', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    expect(w.get('[data-cmd="alignCenter"]').attributes('data-active')).toBe('false')
    await w.get('[data-cmd="alignCenter"]').trigger('click')
    await waitForEditorReactivity()
    expect(w.get('[data-cmd="alignCenter"]').attributes('data-active')).toBe('true')
  })

  // reka's own Popover portal is named Teleport, colliding with VTU's teleport stub, so its
  // content needs renderStubDefaultSlot (already on via globalOpts) to stay queryable; opening
  // both panels here is what makes their own markup visible to the assertion below.
  async function openBothMenus(w: VueWrapper): Promise<void> {
    await w.get('[data-cmd="color"]').trigger('click')
    await w.get('[data-cmd="table"]').trigger('click')
  }

  it('no longer renders PrimeIcons font classes, including inside either open popover panel', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    await openBothMenus(w)
    expect(w.html()).not.toMatch(/class="[^"]*\bpi\b/)
  })

  // Icon-only toolbar buttons need names: an icon with no text node otherwise announces only
  // "button" to a screen reader.
  it('names every icon-only toolbar button', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    for (const cmd of ['alignLeft', 'bulletList', 'orderedList']) {
      const btn = w.get(`[data-cmd="${cmd}"]`)
      expect(btn.attributes('aria-label') || btn.text(), cmd).toBeTruthy()
    }
  })

  // ItemForm.vue wraps every field in <form @submit.prevent>, and this control has no ancestor
  // that self-injects a type onto a bare <button> the way PopoverTrigger's as-child merge does
  // for the two menu triggers below — every one of these needs its own explicit type="button" or
  // its first click submits the whole record instead of running its command.
  it('gives every button-rendered data-cmd control an explicit type="button", toolbar and both open menu panels alike', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    await openBothMenus(w)
    // colorFree is deliberately <input type="color">, not a button (real <button> or the
    // vendored Button's stub tag) — it carries no submit hazard and is asserted separately below.
    const buttons = w.findAll('[data-cmd]').filter((el) => el.element.tagName !== 'INPUT')
    expect(buttons.length).toBeGreaterThan(20)
    buttons.forEach((el) => expect(el.attributes('type'), el.attributes('data-cmd')).toBe('button'))
    expect(w.get('[data-cmd="colorFree"]').attributes('type')).toBe('color')
  })

  // jsdom does not run Tailwind, so no test here can observe which rule actually paints. What it
  // CAN observe is exactly what Button.vue computes at render time — cn(buttonVariants(...),
  // props.class) — which is the twMerge step that decides whether the vendored hover classes
  // survive alongside the toolbar's active-hover override or get de-duplicated away. Both survive
  // here because data-[active=true]:hover:… and dark:data-[active=true]:hover:… are each a
  // different modifier set than the vendored hover:… and dark:hover:…, so twMerge does not treat
  // them as conflicts in the same group; the win is then a CSS-specificity question (see the
  // toolbar's own comment) rather than a class-list question, which is why this test only pins
  // "both present", not "which one applies".
  it('keeps the active-hover override classes alongside the vendored ghost hover classes after cn()', () => {
    const overrideClass = 'data-[active=true]:bg-primary data-[active=true]:text-primary-foreground '
      + 'data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground '
      + 'dark:data-[active=true]:hover:bg-primary'
    const merged = cn(buttonVariants({ variant: 'ghost', size: 'icon' }), overrideClass)
    expect(merged).toContain('hover:bg-accent')
    expect(merged).toContain('dark:hover:bg-accent/50')
    expect(merged).toContain('data-[active=true]:hover:bg-primary')
    expect(merged).toContain('data-[active=true]:hover:text-primary-foreground')
    expect(merged).toContain('dark:data-[active=true]:hover:bg-primary')
  })
})
