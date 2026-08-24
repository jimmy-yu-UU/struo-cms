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
      heading4: 'Heading 4', heading5: 'Heading 5', heading6: 'Heading 6',
      headings: 'Headings', paragraph: 'Body text',
      subscript: 'Subscript', superscript: 'Superscript',
      bulletList: 'Bullet list', numberedList: 'Numbered list',
      blockquote: 'Blockquote', codeBlock: 'Code block',
      link: 'Link', horizontalRule: 'Horizontal rule', insertImage: 'Insert image',
      undo: 'Undo', redo: 'Redo',
      linkPrompt: 'Link URL', insertImageTitle: 'Insert image',
      table: 'Table',
      addRowBefore: 'Add row above', addRowAfter: 'Add row below',
      addColumnBefore: 'Add column left', addColumnAfter: 'Add column right',
      deleteRow: 'Delete row', deleteColumn: 'Delete column',
      toggleHeaderRow: 'Toggle header row', deleteTable: 'Delete table',
      placeholder: 'Write something…',
    },
  } },
  'zh-TW': { fields: { richtext: { placeholder: '開始輸入…' } } } },
})

// Dialog/Popover are reka compound components: DialogContent/PopoverContent inject context that
// only the real DialogRoot/PopoverRoot provides, so those roots are mounted for real rather than
// object-stubbed. Their own portal wrapper is itself named Teleport, so it collides with VTU's
// teleport stub and drops slot content unless renderStubDefaultSlot is on; that in turn also
// renders the default slot of the object-stubbed leaf components below (Button, MediaGrid).
const stubs = { Button: true, MediaGrid: true, teleport: true }
const globalOpts = { plugins: [i18n], stubs, renderStubDefaultSlot: true }

// jsdom implements neither Range method: ProseMirror's own focus command chains a scrollIntoView
// that measures the caret via `Range.getClientRects()`/`getBoundingClientRect()`, and without
// this, that measurement throws asynchronously (inside a requestAnimationFrame callback, so
// outside any promise a test awaits) the moment a test mounts attached to the real document and
// focuses the editor — which the two `attachTo` tests below are the only ones in this file to do.
if (!Range.prototype.getClientRects) {
  Range.prototype.getClientRects = () => [] as unknown as DOMRectList
}
if (!Range.prototype.getBoundingClientRect) {
  Range.prototype.getBoundingClientRect = () => ({
    top: 0, bottom: 0, left: 0, right: 0, width: 0, height: 0, x: 0, y: 0, toJSON: () => undefined,
  }) as DOMRect
}
// jsdom implements neither this: prosemirror-view's posAtCoords (called from the table
// context-menu's contextmenu handler) calls `doc.elementFromPoint(...)` unconditionally -- unlike
// its caret*FromPoint calls a few lines above it, which do feature-check first -- so without this
// stub the "arms its own menu for a right-click inside a table" test below throws an uncaught
// TypeError from inside the event dispatch. Returning null here is what a real browser would do
// for a point with no element under it, and is a safe stand-in since jsdom cannot lay out coordinates.
if (!document.elementFromPoint) {
  document.elementFromPoint = () => null
}

describe('RichTextInput', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })
  afterEach(() => {
    vi.restoreAllMocks()
    i18n.global.locale.value = 'en'
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

  it('offers heading levels 2 through 6 and can return to body text', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as {
      editor: { commands: { selectAll: () => void }; isActive: (n: string, a?: Record<string, unknown>) => boolean }
    }
    await w.get('[data-cmd="headings"]').trigger('click')
    for (const lvl of [2, 3, 4, 5, 6]) {
      await w.get(`[data-cmd="h${lvl}"]`).trigger('click')
      expect(vm.editor.isActive('heading', { level: lvl }), `level ${lvl}`).toBe(true)
      await w.get('[data-cmd="headings"]').trigger('click')
    }
    await w.get('[data-cmd="paragraph"]').trigger('click')
    expect(vm.editor.isActive('paragraph')).toBe(true)
    w.unmount()
  })

  it('keeps the level when the active heading is picked again, and labels the trigger', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { isActive: (n: string, a?: Record<string, unknown>) => boolean } }
    await w.get('[data-cmd="headings"]').trigger('click')
    await w.get('[data-cmd="h3"]').trigger('click')
    expect(vm.editor.isActive('heading', { level: 3 })).toBe(true)
    // The trigger's rendered label reads editor.isActive() through the template, which (unlike
    // reading vm.editor.isActive() directly above) only updates after tiptap/vue-3's two-rAF
    // debounce -- see waitForEditorReactivity below.
    await waitForEditorReactivity()
    expect(w.get('[data-cmd="headings"]').text()).toBe('Heading 3')
    // Re-picking the level that is already active must be a no-op, not a toggle back to paragraph.
    await w.get('[data-cmd="headings"]').trigger('click')
    await w.get('[data-cmd="h3"]').trigger('click')
    expect(vm.editor.isActive('heading', { level: 3 })).toBe(true)
    expect(vm.editor.isActive('paragraph')).toBe(false)
    w.unmount()
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
      'alignJustify', 'headings', 'subscript', 'superscript', 'bulletList', 'orderedList',
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
  // both panels here is what makes their panel-internal controls visible — needed by the
  // type="button" assertion further below, not by the toolbar-icon test immediately following
  // (whose icons sit on the two triggers themselves and are visible unopened).
  async function openBothMenus(w: VueWrapper): Promise<void> {
    await w.get('[data-cmd="color"]').trigger('click')
    await w.get('[data-cmd="table"]').trigger('click')
  }

  it('renders lucide glyphs in the toolbar', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    expect(w.find('[data-cmd="bulletList"] .lucide-list').exists()).toBe(true)
    expect(w.find('[data-cmd="color"] .lucide-baseline').exists()).toBe(true)
    expect(w.find('[data-cmd="table"] .lucide-table').exists()).toBe(true)
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

  // A placeholder is not an accessible name: it disappears the instant the user types into the
  // box, and some screen readers never announce it in the first place.
  it('gives the image-dialog search box an accessible name, not just a placeholder', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    await w.get('[data-cmd="image"]').trigger('click')
    await flushPromises()
    expect(w.get('input').attributes('aria-label')).toBe('Search files…')
  })

  it('applies the typography prose classes to the editable surface', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts })
    await flushPromises()
    // The classes go on the contenteditable element itself, not the padded box around it, so the
    // measure caps the text while the bordered frame stays full width.
    const classes = w.get('.ProseMirror').classes()
    expect(classes).toContain('prose')
    expect(classes).toContain('dark:prose-invert')
    w.unmount()
  })

  it('shows the localized placeholder on an empty document', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '' }, global: globalOpts })
    await flushPromises()
    const p = w.get('.ProseMirror p')
    expect(p.classes()).toContain('is-editor-empty')
    expect(p.attributes('data-placeholder')).toBe('Write something…')
    w.unmount()
  })

  it('does not mark a non-empty document as empty', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts })
    await flushPromises()
    expect(w.get('.ProseMirror p').classes()).not.toContain('is-editor-empty')
    w.unmount()
  })

  it('re-renders the placeholder when the UI locale changes', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '' }, global: globalOpts })
    await flushPromises()
    expect(w.get('.ProseMirror p').attributes('data-placeholder')).toBe('Write something…')
    i18n.global.locale.value = 'zh-TW'
    await flushPromises()
    expect(w.get('.ProseMirror p').attributes('data-placeholder')).toBe('開始輸入…')
    w.unmount()
  })

  // `prose` caps the editable at a 65ch measure while EditorContent's padded box stays full
  // width, so a click landing in that gap must be handed off rather than left dead. Triggering
  // the click directly on the wrapper (not on a descendant) is what makes Vue's @click.self
  // condition (event.target === event.currentTarget) hold in jsdom, even though jsdom cannot lay
  // out the 65ch measure itself to reproduce the gap visually.
  // document.activeElement never changes for a node outside the real DOM tree, so this (and the
  // disabled case below) needs `attachTo: document.body` — every other test in this file mounts
  // detached, which is fine for them but would make a focus assertion vacuously pass on `body`.
  it('focuses the editor when a click lands on the padded wrapper, not the editable', async () => {
    const container = document.body.appendChild(document.createElement('div'))
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts, attachTo: container })
    await flushPromises()
    await w.get('.rich-text__content').trigger('click')
    // tiptap's focus command always defers the actual DOM focus() call to a requestAnimationFrame
    // callback (see @tiptap/core's focus.ts), even for a synchronous editor.commands.focus() call.
    await waitForEditorReactivity()
    expect(document.activeElement).toBe(w.get('.ProseMirror').element)
    w.unmount()
    container.remove()
  })

  // ProseMirror's own EditorView.focus() only calls dom.focus() when the view is editable, so the
  // click handoff on a disabled editor is a verified no-op, not a guess — this pins that.
  it('does not focus a disabled editor when the padded wrapper is clicked', async () => {
    const container = document.body.appendChild(document.createElement('div'))
    const w = mount(RichTextInput, {
      props: { modelValue: '<p>abc</p>', disabled: true }, global: globalOpts, attachTo: container,
    })
    await flushPromises()
    await w.get('.rich-text__content').trigger('click')
    await waitForEditorReactivity()
    expect(document.activeElement).not.toBe(w.get('.ProseMirror').element)
    w.unmount()
    container.remove()
  })

  // Upstream only ever adds `is-editor-empty` to the current textblock, whatever tag it is — the
  // old `p.is-editor-empty:first-child` selector silently excluded any other tag, so switching an
  // empty document's block type to a heading made the placeholder vanish while still empty.
  it('keeps the placeholder visible after changing an empty block to a heading', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '' }, global: globalOpts })
    await flushPromises()
    await w.get('[data-cmd="headings"]').trigger('click')
    await w.get('[data-cmd="h2"]').trigger('click')
    await flushPromises()
    const node = w.get('.ProseMirror').element.firstElementChild as HTMLElement
    expect(node.tagName).toBe('H2')
    expect(node.classList.contains('is-editor-empty')).toBe(true)
    expect(node.getAttribute('data-placeholder')).toBe('Write something…')
    w.unmount()
  })

  // Spec §8 listed the disabled/read-only case as inferred from upstream, not observed. Upstream's
  // `active = editor.isEditable || !showOnlyWhenEditable` (default `showOnlyWhenEditable: true`)
  // means a disabled editor gets no placeholder decoration at all — no `is-editor-empty` class,
  // no `data-placeholder` attribute — which this pins as attribute presence, not painting.
  it('shows no placeholder while disabled', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '', disabled: true }, global: globalOpts })
    await flushPromises()
    const p = w.get('.ProseMirror p')
    expect(p.classes()).not.toContain('is-editor-empty')
    expect(p.attributes('data-placeholder')).toBeUndefined()
    w.unmount()
  })

  it('leaves the native menu alone for a right-click outside a table', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts })
    await flushPromises()
    const p = w.get('.ProseMirror p')
    const ev = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    const stop = vi.spyOn(ev, 'stopPropagation')
    p.element.dispatchEvent(ev)
    // stopPropagation is the mechanism that keeps reka from opening ours and from preventing the
    // browser default; asserting it is asserting the behaviour, not an implementation detail.
    expect(stop).toHaveBeenCalled()
    expect(ev.defaultPrevented).toBe(false)
    w.unmount()
  })

  it('arms its own menu for a right-click inside a table', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="tableInsert"]').trigger('click')
    await flushPromises()
    const cell = w.get('.ProseMirror table td, .ProseMirror table th')
    const ev = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    const stop = vi.spyOn(ev, 'stopPropagation')
    cell.element.dispatchEvent(ev)
    expect(stop).not.toHaveBeenCalled()
    w.unmount()
  })
})
