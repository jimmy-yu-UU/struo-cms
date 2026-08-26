import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises, DOMWrapper, type VueWrapper } from '@vue/test-utils'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import type { Editor } from '@tiptap/vue-3'
import RichTextInput from './RichTextInput.vue'
import { fileContentPath } from '../../lib/richTextImages'
import { buttonVariants } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { RICHTEXT_ACTIVE_BUTTON_CLASS } from './richTextCommands'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: {
    common: { cancel: 'Cancel', confirm: 'Confirm' },
    fields: {
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
      linkDialogTitle: 'Link settings', linkUrlLabel: 'Link URL', insertImageTitle: 'Insert image',
      linkOpenInNewTab: 'Open in new tab', removeLink: 'Remove link',
      linkUrlInvalid: 'Enter a valid http(s) or mailto link.',
      table: 'Table',
      addRowBefore: 'Add row above', addRowAfter: 'Add row below',
      addColumnBefore: 'Add column left', addColumnAfter: 'Add column right',
      deleteRow: 'Delete row', deleteColumn: 'Delete column',
      toggleHeaderRow: 'Toggle header row', deleteTable: 'Delete table',
      tableSizeCols: '{count} column | {count} columns', tableSizeRows: '{count} row | {count} rows',
      customSize: 'Custom size…',
      customSizeTitle: 'Insert table', rows: 'Rows', columns: 'Columns',
      withHeaderRow: 'Include header row',
      sizeOutOfRange: 'Rows and columns must be between {min} and {max}.',
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

  // Rewritten for RT-5.5: the link command opens RichTextLinkDialog instead of window.prompt, so
  // driving it means interacting with the dialog's own fields rather than mocking window.prompt.
  it('rejects a javascript: URL entered in the link dialog (defense-in-depth)', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { chain: () => unknown } }
    const chainSpy = vi.spyOn(vm.editor, 'chain')
    await w.get('[data-cmd="link"]').trigger('click')
    await flushPromises()
    await w.get('[data-testid="href"]').setValue('javascript:alert(1)')
    await w.get('[data-testid="href"]').trigger('blur')
    // The dialog's own guard (RichTextLinkDialog.vue's submit()) refuses to emit `submit` for a
    // rejected scheme, so clicking the (disabled) confirm button never reaches the command, and no
    // chain is ever built.
    await w.get('[data-cmd="linkSubmit"]').trigger('click')
    expect(chainSpy).not.toHaveBeenCalled()
  })

  it('applies an https: URL entered in the link dialog', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { chain: () => unknown } }
    const chainSpy = vi.spyOn(vm.editor, 'chain')
    await w.get('[data-cmd="link"]').trigger('click')
    await flushPromises()
    await w.get('[data-testid="href"]').setValue('https://example.com')
    await w.get('[data-cmd="linkSubmit"]').trigger('click')
    await flushPromises()
    expect(chainSpy).toHaveBeenCalled()
  })

  // The regression a naive dialog-based rewrite can reintroduce: window.prompt was modal, so the
  // selection could never move while it was up. A dialog is not modal -- if the command (or the
  // dialog's own autofocus, which moves DOM focus off the editor) collapsed or moved the ProseMirror
  // selection in that gap, the link would land on a caret or the wrong range instead of the text the
  // user actually selected. This asserts the selection survives the gap, and that the mark applies
  // to exactly the originally selected word once confirmed.
  it('applies the link to the originally selected text, not a collapsed caret, after the async dialog resolves', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>hello world</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    const ed = vm.editor
    let from = -1
    let to = -1
    ed.state.doc.descendants((node, pos) => {
      if (from !== -1 || !node.isText || !node.text) return
      const i = node.text.indexOf('world')
      if (i === -1) return
      from = pos + i
      to = from + 'world'.length
    })
    expect(from).toBeGreaterThan(-1)
    ed.commands.setTextSelection({ from, to })

    await w.get('[data-cmd="link"]').trigger('click')
    await flushPromises()
    // Nothing here blocked -- unlike window.prompt, the click has already returned and the dialog
    // is open. If opening it (or its own autofocus) had collapsed the selection, this would already
    // have caught it before the dialog is even confirmed.
    expect(ed.state.selection.empty).toBe(false)
    expect(ed.state.doc.textBetween(ed.state.selection.from, ed.state.selection.to)).toBe('world')

    await w.get('[data-testid="href"]').setValue('https://example.com')
    await w.get('[data-cmd="linkSubmit"]').trigger('click')
    await flushPromises()

    // Exact string, not just toContain: also proves the link wraps only "world" (not the whole
    // paragraph, not a zero-width fragment) and carries no target/rel -- same-tab was the dialog's
    // default (newTab seeded false), and this mark was never given a target at all.
    expect(ed.getHTML()).toBe('<p>hello <a href="https://example.com">world</a></p>')
  })

  // The other half of what this task wires: editing an already-linked selection must seed the
  // dialog from the EXISTING mark's own href/target (not blank fields) and offer Remove, and Remove
  // must clear the whole link from a caret inside it, not just a zero-length fragment there --
  // exactly what extendMarkRange('link') is for.
  it('seeds the dialog from an existing link mark and removes it via the Remove button', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><a href="https://old.example" target="_blank">old</a></p>' },
      global: globalOpts,
    })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    const ed = vm.editor
    // A caret INSIDE the link, not a selection spanning it: proves extendMarkRange('link'), not an
    // accidentally-wide manual selection, is what makes Remove take the whole mark.
    let caretPos = -1
    ed.state.doc.descendants((node, pos) => {
      if (caretPos !== -1 || !node.isText || !node.text) return
      const i = node.text.indexOf('old')
      if (i === -1) return
      caretPos = pos + i + 1
    })
    expect(caretPos).toBeGreaterThan(-1)
    ed.commands.setTextSelection(caretPos)

    await w.get('[data-cmd="link"]').trigger('click')
    await flushPromises()
    expect(w.get<HTMLInputElement>('[data-testid="href"]').element.value).toBe('https://old.example')
    expect(w.get('[role="checkbox"]').attributes('data-state')).toBe('checked')
    expect(w.find('[data-cmd="linkRemove"]').exists()).toBe(true)

    await w.get('[data-cmd="linkRemove"]').trigger('click')
    await flushPromises()
    expect(ed.getHTML()).toBe('<p>old</p>')
  })

  // The batch's headline behaviour, previously asserted nowhere in this suite: checking "open in new
  // tab" must show up as target="_blank" on the stored mark, with no rel riding along (rel is
  // backend-owned -- see richTextCommands.ts). Exact string, not toContain, so this also pins that
  // HTMLAttributes: { rel: null } (RichTextInput.vue's Link.configure) is doing something on the one
  // branch where it actually matters: a brand-new mark, not an edited one.
  it('checking the new-tab box in the link dialog stores target="_blank" with no rel', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>hello world</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    const ed = vm.editor
    let from = -1
    let to = -1
    ed.state.doc.descendants((node, pos) => {
      if (from !== -1 || !node.isText || !node.text) return
      const i = node.text.indexOf('world')
      if (i === -1) return
      from = pos + i
      to = from + 'world'.length
    })
    expect(from).toBeGreaterThan(-1)
    ed.commands.setTextSelection({ from, to })

    await w.get('[data-cmd="link"]').trigger('click')
    await flushPromises()
    await w.get('[data-testid="href"]').setValue('https://example.com')
    await w.get('[role="checkbox"]').trigger('click')
    await w.get('[data-cmd="linkSubmit"]').trigger('click')
    await flushPromises()

    expect(ed.getHTML()).toBe('<p>hello <a target="_blank" href="https://example.com">world</a></p>')
  })

  // The one direction setMark's attribute-merge semantics could plausibly break: @tiptap/core's
  // setMark does `type.create({ ...mark.attrs, ...attributes })`, so unchecking an already-blank
  // link must actually clear target in the new attributes object, not leave the existing mark's own
  // target="_blank" merged back in underneath it.
  it('unchecking the new-tab box on an existing target="_blank" link removes the target', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><a href="https://old.example" target="_blank">old</a></p>' },
      global: globalOpts,
    })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    const ed = vm.editor
    let caretPos = -1
    ed.state.doc.descendants((node, pos) => {
      if (caretPos !== -1 || !node.isText || !node.text) return
      const i = node.text.indexOf('old')
      if (i === -1) return
      caretPos = pos + i + 1
    })
    expect(caretPos).toBeGreaterThan(-1)
    ed.commands.setTextSelection(caretPos)

    await w.get('[data-cmd="link"]').trigger('click')
    await flushPromises()
    expect(w.get('[role="checkbox"]').attributes('data-state')).toBe('checked')
    await w.get('[role="checkbox"]').trigger('click')
    await w.get('[data-cmd="linkSubmit"]').trigger('click')
    await flushPromises()

    expect(ed.getHTML()).toBe('<p><a href="https://old.example">old</a></p>')
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

  it('serializes an image width set via setImage, so a resized image survives the round trip', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.setImage({ src: 'https://example.com/cat.png', width: 480, height: 320 })
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    const html = String(emitted!.at(-1)![0])
    expect(html).toContain('width="480"')
  })

  // Task 1's sanitizer allowlist has no `height` at all, so a naive reading of this test would
  // pass even if the editor still emitted one -- the backend would just strip it on save. What
  // this guards is different: getHTML() must already agree with the stored value *before* any
  // round trip, because the `watch(() => props.modelValue)` comparison further down diffs the
  // two directly. Proof this isn't vacuous: deleting the `rendered: false` override below turns
  // this red (confirmed in this session, Task 2).
  it('never serializes height, even when the inserted node carries one', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.setImage({ src: 'https://example.com/cat.png', width: 480, height: 320 })
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    const html = String(emitted!.at(-1)![0])
    expect(html).not.toContain('height')
  })

  // Guards against a future edit dropping `resize.enabled` silently: if it were removed, the two
  // tests above would still pass (width/height serialization doesn't depend on the node view at
  // all -- confirmed this session that getHTML() serializes via schema.toDOM, never through a
  // live node view), so nothing else in this file would go red.
  it('constructs a resizable node view for an inserted image', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.setImage({ src: 'https://example.com/cat.png' })
    await flushPromises()
    expect(w.find('[data-resize-container]').exists()).toBe(true)
  })

  // Regression guard: a field that mounts already `disabled` must never grow live, draggable
  // handles. This is an existence assertion (no [data-resize-handle] node at all), not an
  // appearance one, so it holds even though jsdom applies no CSS.
  it('renders no resize handles when mounted already disabled', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png"></p>', disabled: true },
      global: globalOpts,
    })
    await flushPromises()
    expect(w.findAll('[data-resize-handle]').length).toBe(0)
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
    await w.get('[data-cell="3-3"]').trigger('click')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    const html = String(emitted!.at(-1)![0])
    expect(html).toContain('<table')
    expect(html).toContain('<th')
  })

  // A non-square pick, unlike the 3x3 test above: a rows/cols swap in onTableInsert, or
  // hardcoding a size and ignoring the payload entirely, would still pass a 3x3 assertion (it's
  // symmetric) but fails this one. Counts observed directly from TipTap's own output for a
  // { rows: 2, cols: 4 } insert: 2 <tr> (one header row, one body row) and 4 <th> (the header
  // row's cells; the body row's 4 cells are <td>, not <th>).
  it('inserts a non-square table matching the picked rows and cols, not a fixed or swapped size', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cell="2-4"]').trigger('click')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    const html = String(emitted!.at(-1)![0])
    expect((html.match(/<tr>/g) ?? []).length).toBe(2)
    expect((html.match(/<th\b/g) ?? []).length).toBe(4)
    w.unmount()
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

  it('flags active toolbar state via data-active for a mark command (bold)', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    expect(w.get('[data-cmd="bold"]').attributes('data-active')).toBe('false')
    await w.get('[data-cmd="bold"]').trigger('click')
    await waitForEditorReactivity()
    expect(w.get('[data-cmd="bold"]').attributes('data-active')).toBe('true')
  })

  // bold and alignCenter now render through the same RichTextCommandButton, from the same
  // TOOLBAR_BEFORE_HEADINGS v-for (see richTextCommands.ts and RichTextInput.vue's template), so
  // this pair no longer contrasts a hand-written control against a templated one -- that contrast
  // no longer exists. What it still contrasts is the two commands' own isActive checks: bold's is
  // editor.isActive('bold'), a mark check, while alignCenter's is
  // editor.isActive({ textAlign: 'center' }), a node-attribute check -- two different TipTap
  // active-state APIs feeding the same data-active binding, so a regression that broke one path
  // without breaking the other would still be caught by keeping both tests. A fresh mount (rather
  // than chaining onto the bold click above) sidesteps tiptap's stored-mark semantics: toggling
  // bold with no text selected only stores it as a pending mark for the next typed character, and
  // a later, unrelated command clears that pending mark — real editor behaviour, not something
  // this migration changed, but it would make a combined assertion flaky for reasons unrelated to
  // data-active.
  it('flags active toolbar state via data-active for a node-attribute command (alignCenter)', async () => {
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
  // comment above RICHTEXT_ACTIVE_BUTTON_CLASS in richTextCommands.ts) rather than a class-list
  // question, which is why this test only pins "both present", not "which one applies".
  // RICHTEXT_ACTIVE_BUTTON_CLASS (imported above), not a retyped copy of the string: that way this
  // test exercises the exact class list the toolbar actually ships, and would fail if a future edit
  // changed what the constant contains. The expect(...).toContain(...) lines below stay hardcoded
  // on purpose, rather than also being derived from the constant: richTextCommands.test.ts already
  // pins the constant's own exact value, so hardcoding the substrings here means the two tests
  // catch a bad edit from both directions — that test if the constant's value itself changes, this
  // one if cn() ever stops preserving those substrings even though the constant did not change.
  it('keeps the active-hover override classes alongside the vendored ghost hover classes after cn()', () => {
    const merged = cn(buttonVariants({ variant: 'ghost', size: 'icon' }), RICHTEXT_ACTIVE_BUTTON_CLASS)
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

  // This disabled/read-only case is read out of upstream's own source, not observed by running a
  // real placeholder decoration first: upstream's `active = editor.isEditable ||
  // !showOnlyWhenEditable` (default `showOnlyWhenEditable: true`) means a disabled editor gets no
  // placeholder decoration at all — no `is-editor-empty` class, no `data-placeholder` attribute —
  // which this pins as attribute presence, not painting.
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
    await w.get('[data-cell="3-3"]').trigger('click')
    await flushPromises()
    const cell = w.get('.ProseMirror table td, .ProseMirror table th')
    const ev = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    const stop = vi.spyOn(ev, 'stopPropagation')
    cell.element.dispatchEvent(ev)
    expect(stop).not.toHaveBeenCalled()
    // Not-stopped alone is a proxy that would also pass if the handler were never attached, or
    // took the `!ed` early-return path -- neither of which says our menu actually armed. reka's
    // ContextMenuTrigger.handleContextMenu is itself async (it awaits Vue's own nextTick before
    // opening and calling preventDefault), so the decision isn't observable until this test also
    // awaits a tick.
    // One tick is enough for reka's own async handleContextMenu to run its preventDefault --
    // observed directly: ev.defaultPrevented was already true after a single `await nextTick()`.
    // But that call also assigns rootContext's `open` ref, and that ref's own re-render (the one
    // that actually mounts the menu's items into the DOM) is queued onto Vue's *next* flush rather
    // than running inside the same continuation -- observed directly too: with only one tick,
    // `data-state` on the trigger was still "closed" and no `data-cmd="table-*"` item existed yet.
    // A second tick is what lets that follow-on render flush.
    await nextTick()
    await nextTick()
    expect(w.find('[data-cmd="table-deleteRow"]').exists()).toBe(true)
    expect(ev.defaultPrevented).toBe(true)
    w.unmount()
  })

  // posAtCoords needs real layout to resolve accurate coordinates, and jsdom lays nothing out --
  // that half (does the reported position correspond to the cell actually under the cursor?)
  // needs a live browser check, not a jsdom test. What jsdom CAN pin, by stubbing posAtCoords itself,
  // is the wiring around it: whatever position it resolves to must become the selection, because
  // that is what makes a table action apply to the cell the user actually right-clicked rather
  // than wherever the caret happened to be already. Tried first: letting jsdom's own
  // posAtCoords run unstubbed against zero-size layout rects -- it returns an arbitrary non-null
  // position regardless of the two lines under test, which is exactly why this test replaces it
  // with a controlled stub instead of trusting jsdom's coordinate math.
  it('moves the selection to whatever position posAtCoords resolves, on a right-click inside a table', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cell="3-3"]').trigger('click')
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    const ed = vm.editor
    // somePos: the text position just inside the LAST tableCell/tableHeader node in the document,
    // derived from the real doc rather than hardcoded. `pos` from descendants is the position
    // right before the node opens; +1 enters the cell to its (empty) paragraph child, +2 enters
    // that paragraph's own (empty) content -- a valid, resolvable TextSelection anchor. Picking the
    // LAST cell (not the first) matters: right after insertTable the selection already sits inside
    // the first cell, so asserting against the first cell could pass even if the two lines under
    // test never ran.
    let somePos = -1
    ed.state.doc.descendants((node, pos) => {
      if (node.type.name === 'tableCell' || node.type.name === 'tableHeader') somePos = pos + 2
    })
    expect(somePos).toBeGreaterThan(-1)
    expect(ed.state.selection.from).not.toBe(somePos)
    vi.spyOn(ed.view, 'posAtCoords').mockReturnValue({ pos: somePos, inside: -1 })
    const cell = w.get('.ProseMirror table td, .ProseMirror table th')
    const ev = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    cell.element.dispatchEvent(ev)
    expect(ed.state.selection.from).toBe(somePos)
    w.unmount()
  })

  it('inserts a custom-sized table from the dialog', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="tableCustomSize"]').trigger('click')
    await flushPromises()
    await w.get('[data-cmd="tableSizeConfirm"]').trigger('click')
    await flushPromises()
    const vm = w.vm as unknown as { editor: { getHTML: () => string } }
    const html = vm.editor.getHTML()
    expect(html).toContain('<table')
    expect((html.match(/<tr>/g) ?? []).length).toBe(3)
    // 3 <tr> alone is true whether withHeaderRow is true or false -- this is what actually
    // discriminates the dialog's default-checked header box reaching insertTable.
    expect(html).toContain('<th')
    w.unmount()
  })

  // Task 1 makes the server wrap header rows in <thead>, so that is now what arrives in modelValue.
  // TipTap's table schema has no thead node, so the parser must descend through it and keep the
  // cells as header cells -- if it instead dropped them or downgraded them to td, every save after
  // a load would destroy the header. ProseMirror's parser is documented to descend through
  // elements with no matching rule, but this pins the actual behaviour of the installed version
  // rather than trusting that.
  it('parses a server-normalized <thead> back into a header row', async () => {
    const stored = '<table><thead><tr><th>H1</th><th>H2</th></tr></thead>'
                 + '<tbody><tr><td>a</td><td>b</td></tr></tbody></table>'
    const w = mount(RichTextInput, { props: { modelValue: stored }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: { getHTML: () => string } }
    const html = vm.editor.getHTML()

    // Header cells survive as header cells, with their text intact, not just as empty tags --
    // `/<th[\s>]/` so this can never match `<thead`, since the "not <thead" check below is the
    // only thing standing between a false pass and a wrapped-in-thead-again regression otherwise.
    expect(html).toContain('<th')
    expect((html.match(/<th[\s>]/g) ?? []).length).toBe(2)
    // "H1" surviving toContain(html) alone would also pass if it landed outside any <th> at all --
    // assert it falls inside the first header cell's own tag pair instead.
    const firstTh = html.indexOf('<th')
    const firstThClose = html.indexOf('</th>', firstTh)
    expect(firstThClose).toBeGreaterThan(firstTh)
    expect(html.slice(firstTh, firstThClose)).toContain('H1')
    expect(html).toContain('H2')
    // ...the body row is untouched...
    expect((html.match(/<td/g) ?? []).length).toBe(2)
    // ...the header row still comes before the body row, not reordered -- `/<th[\s>]/` again so a
    // stray `<thead` (which this test already asserts is absent) could never be mistaken for it...
    expect(html.search(/<th[\s>]/)).toBeLessThan(html.indexOf('<td'))
    // ...and TipTap re-serializes without the thead, which is exactly why Task 1 lives on the
    // server and why the editor needs its own tbody-th styling rule.
    expect(html).not.toContain('<thead')
  })

  // BubbleMenuPlugin's own update() (registered by RichTextBubbleMenu, mounted in RichTextInput's
  // template) dispatches through window.setTimeout at updateDelay's default of 250ms whenever the
  // selection is non-collapsed -- selectAll() below produces exactly that case, so a synchronous
  // assertion right after selecting/focusing would still see the pre-selection (hidden) state.
  // Real timers, not vitest's fake ones: the wait here is on that 250ms window.setTimeout inside
  // handleDebouncedUpdate. Fake timers were not attempted. 300ms clears the 250ms window with margin.
  async function settleBubbleMenu(): Promise<void> {
    await new Promise((resolve) => { setTimeout(resolve, 300) })
    await flushPromises()
  }

  // Scoped to the menu's own root, not a bare `[data-cmd]`: the toolbar renders the same data-cmd
  // values while the menu is open, so an unscoped query would be ambiguous. Queries document.body,
  // not the wrapper: RichTextBubbleMenu appends its BubbleMenuPlugin element into a private
  // container that is itself a child of document.body, so once shown the menu's root is a
  // descendant of body, not of anything mount() attached -- w.get() would never find it.
  function bubbleRoot() {
    return new DOMWrapper(document.body).get('.rich-text__bubble')
  }

  // Neither RichTextBubbleMenu.test.ts (no RichTextInput, no commandContext, no toolbar) nor this
  // file's own toolbar tests above (which never select text or open the bubble menu) can see the
  // two components actually wired together -- this proves the whole path once, for both a plain
  // command and link, which is the one command in the inline group that depends on the context
  // RichTextInput builds and does not export (richTextCommands.ts's RichTextCommandContext).
  it('shows the bubble menu over a real selection, and a click through it runs the command on the document', async () => {
    const container = document.body.appendChild(document.createElement('div'))
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts, attachTo: container })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.selectAll()
    vm.editor.commands.focus()
    await settleBubbleMenu()

    // A non-link command: toggling it is only observable if the click actually reached the real
    // editor through RichTextBubbleMenu's `run` emit and RichTextInput's `runCommand` -- a wiring
    // mistake that drops the emit, or that never calls `command.run`, leaves this false.
    await bubbleRoot().get('[data-cmd="italic"]').trigger('click')
    expect(vm.editor.isActive('italic')).toBe(true)

    // link's own run() touches the editor first (editor.getAttributes('link')/isActive('link'), to
    // seed the dialog) and only then calls ctx.openLinkDialog (richTextCommands.ts) -- asserting the
    // seeded values proves runCommand supplied the real commandContext RichTextInput builds (a
    // brand-new selection carrying no link mark: an empty href, no Remove button), not an empty
    // stand-in that would either throw or leave the dialog showing whatever it last held.
    await bubbleRoot().get('[data-cmd="link"]').trigger('click')
    await flushPromises()
    expect(w.get<HTMLInputElement>('[data-testid="href"]').element.value).toBe('')
    expect(w.find('[data-cmd="linkRemove"]').exists()).toBe(false)
    await w.get('[data-testid="href"]').setValue('https://example.com')
    await w.get('[data-cmd="linkSubmit"]').trigger('click')
    await flushPromises()
    expect(vm.editor.isActive('link')).toBe(true)

    w.unmount()
    container.remove()
  })

  // The toolbar and the bubble menu render the same data-cmd values while the menu is
  // open. Neither component's own test can see the two coexisting -- RichTextBubbleMenu.test.ts
  // mounts no toolbar, and the toolbar tests above never open the bubble menu -- so a wiring
  // mistake that fires a command through both surfaces for one click (toggling bold back off), or
  // that leaves the toolbar's own reactive state stale after a command run through the OTHER
  // surface, is invisible anywhere else in this suite.
  it('does not double-fire a command shared with the toolbar, and the toolbar reflects a change made through the menu', async () => {
    const container = document.body.appendChild(document.createElement('div'))
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts, attachTo: container })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.selectAll()
    vm.editor.commands.focus()
    await settleBubbleMenu()

    // document.body, not w: the container this test attaches (itself a child of document.body,
    // see above) holds the toolbar's own button, while the bubble menu's is appended into
    // RichTextBubbleMenu's own private container, itself a child of document.body -- both are
    // within document.body's subtree, so querying it is what counts one of each rather than
    // missing the bubble menu's entirely.
    expect(new DOMWrapper(document.body).findAll('[data-cmd="bold"]')).toHaveLength(2)
    await bubbleRoot().get('[data-cmd="bold"]').trigger('click')
    // Toggled ON, not on-and-off: a click that fired the command twice (once through each
    // surface) would leave this false instead.
    expect(vm.editor.isActive('bold')).toBe(true)
    await waitForEditorReactivity()
    expect(w.get('.rich-text__toolbar [data-cmd="bold"]').attributes('data-active')).toBe('true')

    w.unmount()
    container.remove()
  })

  // RT-5's own final review left this open: BubbleMenuPlugin arms `preventHide` on its own
  // mousedown, which swallows the very next blur, so the dialog's autofocus stealing DOM focus from
  // the editor would not, on its own, hide this menu -- it would linger beside the open dialog.
  // A bare VTU `.trigger('click')` dispatches only a 'click' event, no 'mousedown' -- so it does
  // NOT arm preventHide, and the resulting blur hides the menu on its own regardless of whether
  // openLinkDialog's hide() call exists at all (confirmed: deleting that call left this test green).
  // Dispatching 'mousedown' first, as a real click does, is what arms preventHide and makes the
  // blur path a no-op -- only then does reaching this assertion prove hide() (and the matching
  // pluginKey string on both ends) is doing the work.
  it('hides the bubble menu when its own link button opens the dialog', async () => {
    const container = document.body.appendChild(document.createElement('div'))
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts, attachTo: container })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.selectAll()
    vm.editor.commands.focus()
    await settleBubbleMenu()
    // bubbleRoot() itself throws if the menu is absent, so reaching the next line already proves
    // it is showing.
    const linkBtn = bubbleRoot().get('[data-cmd="link"]').element
    linkBtn.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }))
    linkBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()
    // bubbleRoot() itself uses .get(), which throws rather than reporting absence -- .find() is
    // what actually lets this assert the menu is gone, not merely still present.
    expect(new DOMWrapper(document.body).find('.rich-text__bubble').exists()).toBe(false)

    w.unmount()
    container.remove()
  })

  // reka points DialogContent's aria-describedby at a DialogDescription id whether or not one is
  // rendered, and warns on mount when nothing in the document carries that id -- so the warning is
  // not cosmetic: without a description, assistive tech follows a dangling reference.
  //
  // Mounted attached, unlike most tests in this file, and that is load-bearing: reka resolves the
  // id with document.getElementById, which cannot see a detached wrapper. Mounted the usual way
  // this assertion fails whether or not the description exists, so it would prove nothing.
  it('renders a description on the image dialog, so reka does not warn about a dangling aria-describedby', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const container = document.body.appendChild(document.createElement('div'))
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts, attachTo: container })
    await flushPromises()
    await w.get('[data-cmd="image"]').trigger('click')
    await flushPromises()
    const messages = warn.mock.calls.map((c) => c.map(String).join(' '))
    expect(messages.filter((m) => m.includes('Missing `Description`'))).toEqual([])
    w.unmount()
    container.remove()
  })
})
