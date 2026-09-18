import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises, DOMWrapper, type VueWrapper } from '@vue/test-utils'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { VueRenderer, type Editor } from '@tiptap/vue-3'
import RichTextInput from './RichTextInput.vue'
import { RichTextSlashExtension } from './richTextSlashExtension'
import richTextInputSource from './RichTextInput.vue?raw'
import RichTextContextMenu from './RichTextContextMenu.vue'
import RichTextImageAltDialog from './RichTextImageAltDialog.vue'
import RichTextLinkDialog from './RichTextLinkDialog.vue'
import RichTextTableSizeDialog from './RichTextTableSizeDialog.vue'
import type { ImageAction } from './richTextImageActions'
import { fileContentPath } from '../../lib/richTextImages'
import { buttonVariants } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { RICHTEXT_ACTIVE_BUTTON_CLASS } from './richTextCommands'

// tiptap schedules onCreate on a setTimeout(0). flushPromises() resolves via setImmediate, whose
// ordering against a timers-phase callback is not guaranteed, so it is not a reliable wait for it;
// a second setTimeout(0) is, because same-delay timers fire in registration order.
function waitForEditorCreate(): Promise<void> {
  return new Promise((resolve) => { setTimeout(resolve, 0) })
}

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
      slashMenu: 'Insert block', slashNoResults: 'No matching commands',
    },
  } },
  // Only the keys the slash tests need translated: fallbackLocale 'en' supplies the rest, and the
  // alias test below is only a test of the alias path while the zh-TW label carries no ASCII.
  'zh-TW': { fields: { richtext: { placeholder: '開始輸入…', table: '表格' } } } },
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
  // A handful of tests below need `attachTo` a real, document-attached host (document.activeElement
  // never moves for a detached tree, and reka resolves ids with document.getElementById). Torn down
  // from afterEach rather than at the end of the test body, because a body-level teardown is skipped
  // the moment an assertion above it throws -- which would leave that test's whole editor DOM in the
  // document for every later test in the file to match against. Same reason vitest.setup.ts unmounts
  // wrappers from a global afterEach instead of trusting each test to do it.
  const attachedContainers: HTMLElement[] = []

  function attachContainer(): HTMLElement {
    const el = document.body.appendChild(document.createElement('div'))
    attachedContainers.push(el)
    return el
  }

  beforeEach(() => {
    setActivePinia(createPinia())
  })
  afterEach(() => {
    vi.restoreAllMocks()
    // restoreMocks/clearMocks (vite.config.ts) reset spies but not the clock, so the tests that
    // call useBubbleMenuClock() below need this to hand the real one back.
    //
    // Ordering hazard if a clocked test ever mounts a component that cancels a timer on unmount:
    // this suite-level hook runs BEFORE vitest.setup.ts's file-level enableAutoUnmount, so on a
    // thrown assertion the unmount happens with the real clock already restored, and a
    // clearTimeout() holding a fake id would hand the real clearTimeout a small integer that can
    // collide with an unrelated live timer. RichTextInput.vue's onBeforeUnmount is that shape
    // (debouncedLoadImages.cancel()); none of the clocked tests below reach it today, because none
    // of them opens the image dialog whose search input arms that debounce.
    vi.useRealTimers()
    attachedContainers.splice(0).forEach((el) => { el.remove() })
    i18n.global.locale.value = 'en'
  })

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

  // Checking "open in new
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

  // Not a duplicate of the sanitizer's height strip: getHTML() must already agree with the stored
  // value BEFORE any round trip, because watch(() => props.modelValue) diffs the two directly.
  // Fails if the `rendered: false` override on the height attribute is dropped.
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

  // The only test that catches `resize.enabled` being dropped: getHTML() serializes via
  // schema.toDOM, never through a live node view, so the width/height tests above stay green
  // without it.
  it('constructs a resizable node view for an inserted image', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.setImage({ src: 'https://example.com/cat.png' })
    await flushPromises()
    expect(w.find('[data-resize-container]').exists()).toBe(true)
  })

  // Pins the `directions` option, not appearance -- jsdom applies no CSS, so this can only see
  // that a handle element was attached per direction, never that it is visible or grabbable.
  it('renders all eight resize handles -- four corners and four edge midpoints -- for an inserted image', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.setImage({ src: 'https://example.com/cat.png' })
    await flushPromises()
    const handles = w.findAll('[data-resize-handle]')
      .map((h) => h.attributes('data-resize-handle'))
      .sort()
    expect(handles).toEqual(
      ['bottom', 'bottom-left', 'bottom-right', 'left', 'right', 'top', 'top-left', 'top-right'],
    )
  })

  // A field that mounts already `disabled` must never grow live, draggable handles. Existence,
  // not appearance -- jsdom applies no CSS.
  it('renders no resize handles when mounted already disabled', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png"></p>', disabled: true },
      global: globalOpts,
    })
    await flushPromises()
    await waitForEditorCreate()
    expect(w.findAll('[data-resize-handle]').length).toBe(0)
  })

  // The setEditable() that removes those handles emits 'update'. Unsuppressed, that reaches
  // emitNormalized() and fakes an unsaved change for every rich-text field a read-only user opens.
  it('emits no update:modelValue when mounted already disabled', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png"></p>', disabled: true },
      global: globalOpts,
    })
    await flushPromises()
    await waitForEditorCreate()
    expect(w.emitted('update:modelValue')).toBeUndefined()
  })

  // Same defect through the watch(() => props.disabled) door, which needs its own suppression.
  it('emits no update:modelValue when disabled turns on after mount', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png"></p>', disabled: false },
      global: globalOpts,
    })
    await flushPromises()
    await waitForEditorCreate()
    await w.setProps({ disabled: true })
    await flushPromises()
    expect(w.emitted('update:modelValue')).toBeUndefined()
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

  // bold and alignCenter render through the same RichTextCommandButton, from the same
  // TOOLBAR_BEFORE_HEADINGS v-for (see richTextCommands.ts and RichTextInput.vue's template), so
  // this pair does not contrast a hand-written control against a templated one. What it contrasts
  // is the two commands' own isActive checks: bold's is
  // editor.isActive('bold'), a mark check, while alignCenter's is
  // editor.isActive({ textAlign: 'center' }), a node-attribute check -- two different TipTap
  // active-state APIs feeding the same data-active binding, so a regression that broke one path
  // without breaking the other would still be caught by keeping both tests. A fresh mount (rather
  // than chaining onto the bold click above) sidesteps tiptap's stored-mark semantics: toggling
  // bold with no text selected only stores it as a pending mark for the next typed character, and
  // a later, unrelated command clears that pending mark — real editor behaviour, not an artifact
  // introduced here, but it would make a combined assertion flaky for reasons unrelated to
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

  // The insert-image dialog shares the alt/link dialogs' blur-then-restore helpers; this pins the
  // same synchronous destination for its own entry point.
  it('blurs the editor before the insert-image dialog opens', async () => {
    const container = attachContainer()
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts, attachTo: container })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.focus()
    await waitForEditorReactivity()
    expect(document.activeElement).toBe(w.get('.ProseMirror').element)
    const clicked = w.get('[data-cmd="image"]').trigger('click')
    expect(document.activeElement).toBe(document.body)
    await clicked
    w.unmount()
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
    const container = attachContainer()
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts, attachTo: container })
    await flushPromises()
    await w.get('.rich-text__content').trigger('click')
    // tiptap's focus command always defers the actual DOM focus() call to a requestAnimationFrame
    // callback (see @tiptap/core's focus.ts), even for a synchronous editor.commands.focus() call.
    await waitForEditorReactivity()
    expect(document.activeElement).toBe(w.get('.ProseMirror').element)
    w.unmount()
  })

  // ProseMirror's own EditorView.focus() only calls dom.focus() when the view is editable, so the
  // click handoff on a disabled editor is a verified no-op, not a guess — this pins that.
  it('does not focus a disabled editor when the padded wrapper is clicked', async () => {
    const container = attachContainer()
    const w = mount(RichTextInput, {
      props: { modelValue: '<p>abc</p>', disabled: true }, global: globalOpts, attachTo: container,
    })
    await flushPromises()
    await w.get('.rich-text__content').trigger('click')
    await waitForEditorReactivity()
    expect(document.activeElement).not.toBe(w.get('.ProseMirror').element)
    w.unmount()
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

  // Dispatches a bare contextmenu MouseEvent at an element with a stopPropagation spy attached.
  // The nextTick is required: `target` reaches the child as a Vue prop update, so without it every
  // props('target') assertion below reads the PRE-dispatch value.
  async function dispatchContextMenu(el: Element): Promise<ReturnType<typeof vi.spyOn>> {
    const ev = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    const stop = vi.spyOn(ev, 'stopPropagation')
    el.dispatchEvent(ev)
    await nextTick()
    return stop
  }

  it('routes a right-click directly on an image to the image menu, not stopped', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png"></p>' },
      global: globalOpts,
    })
    await flushPromises()
    const stop = await dispatchContextMenu(w.get('.ProseMirror img').element)
    expect(stop).not.toHaveBeenCalled()
    expect(w.findComponent(RichTextContextMenu).props('target')).toBe('image')
    const vm = w.vm as unknown as { editor: Editor }
    expect(vm.editor.state.selection.constructor.name).toBe('NodeSelection')
    w.unmount()
  })

  // The routing-order lock: an <img> inside a table cell must route to the image menu even though
  // isInEditorTable also matches it. Swapping the two checks in onContentContextMenu turns this red.
  it('routes a right-click on an image inside a table cell to the image menu, not the table menu', async () => {
    const w = mount(RichTextInput, {
      props: {
        modelValue: '<table><tbody><tr><td><img src="https://example.com/cat.png"></td></tr></tbody></table>',
      },
      global: globalOpts,
    })
    await flushPromises()
    await dispatchContextMenu(w.get('.ProseMirror table img').element)
    expect(w.findComponent(RichTextContextMenu).props('target')).toBe('image')
    w.unmount()
  })

  it('routes a right-click on a non-image table cell to the table menu', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
    await flushPromises()
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cell="3-3"]').trigger('click')
    await flushPromises()
    await dispatchContextMenu(w.get('.ProseMirror table td, .ProseMirror table th').element)
    expect(w.findComponent(RichTextContextMenu).props('target')).toBe('table')
    w.unmount()
  })

  it('routes a right-click on a plain paragraph to neither menu, and stops it', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png"></p><p>abc</p>' },
      global: globalOpts,
    })
    await flushPromises()
    // Arm the image menu first, so the assertion below actually pins that a later paragraph click
    // resets `target` back to null rather than merely observing its untouched initial value.
    await dispatchContextMenu(w.get('.ProseMirror img').element)
    expect(w.findComponent(RichTextContextMenu).props('target')).toBe('image')
    const stop = await dispatchContextMenu(w.findAll('.ProseMirror p').at(-1)!.element)
    expect(stop).toHaveBeenCalled()
    expect(w.findComponent(RichTextContextMenu).props('target')).toBeNull()
    w.unmount()
  })

  // Both disabled branches clear `target`, and this pins that they agree. Arming the image menu
  // while the field is still editable is what makes it discriminate: without the reset in the
  // branch under test, the stale list survives the transition to read-only.
  it.each([
    ['image', '.ProseMirror img'],
    ['table cell', '.ProseMirror table td'],
  ])('clears the context-menu target for a right-click on a %s in a disabled field', async (_label, selector) => {
    const w = mount(RichTextInput, {
      props: {
        modelValue: '<p><img src="https://example.com/cat.png"></p>'
          + '<table><tbody><tr><td>c</td></tr></tbody></table>',
      },
      global: globalOpts,
    })
    await flushPromises()
    await dispatchContextMenu(w.get('.ProseMirror img').element)
    expect(w.findComponent(RichTextContextMenu).props('target')).toBe('image')
    await w.setProps({ disabled: true })
    await flushPromises()
    await dispatchContextMenu(w.get(selector).element)
    expect(w.findComponent(RichTextContextMenu).props('target')).toBeNull()
    w.unmount()
  })

  it('does not move the selection or stop the event for a right-click on an image in a disabled field', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png"></p>', disabled: true },
      global: globalOpts,
    })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    const before = vm.editor.state.selection.from
    const stop = await dispatchContextMenu(w.get('.ProseMirror img').element)
    expect(stop).not.toHaveBeenCalled()
    expect(vm.editor.state.selection.from).toBe(before)
    w.unmount()
  })

  it('deleteImage removes the selected image from the emitted HTML', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png"></p>' },
      global: globalOpts,
    })
    await flushPromises()
    await dispatchContextMenu(w.get('.ProseMirror img').element)
    const menu = w.findComponent(RichTextContextMenu)
    expect(menu.props('target')).toBe('image')
    const menuVm = menu.vm as unknown as { runImage: (action: ImageAction) => void }
    menuVm.runImage('deleteImage')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    const html = String(emitted!.at(-1)![0])
    expect(html).not.toContain('<img')
    w.unmount()
  })

  it('editAlt opens the alt dialog seeded with the right-clicked image\'s current alt', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png" alt="a cat"></p>' },
      global: globalOpts,
    })
    await flushPromises()
    await dispatchContextMenu(w.get('.ProseMirror img').element)
    const menuVm = w.findComponent(RichTextContextMenu).vm as unknown as {
      runImage: (action: ImageAction) => void
    }
    menuVm.runImage('editAlt')
    await flushPromises()
    const dialog = w.findComponent(RichTextImageAltDialog)
    expect(dialog.props('open')).toBe(true)
    expect(dialog.props('alt')).toBe('a cat')
    w.unmount()
  })

  // Drives the dialog's real submit button, so this is also the test that fails if
  // onImageAltDialogOpenChange's release of the captured image node ever runs before
  // onImageAltDialogSubmit -- either by moving the clear earlier or by inverting the dialog's own
  // emit order. Under that mutation the guard rejects the write and no alt is emitted at all.
  it('submitting the alt dialog updates the emitted HTML with the new alt', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png" alt="old"></p>' },
      global: globalOpts,
    })
    await flushPromises()
    await dispatchContextMenu(w.get('.ProseMirror img').element)
    const menuVm = w.findComponent(RichTextContextMenu).vm as unknown as {
      runImage: (action: ImageAction) => void
    }
    menuVm.runImage('editAlt')
    await flushPromises()
    await w.get('[data-testid="alt"]').setValue('new alt')
    await w.get('[data-cmd="altSubmit"]').trigger('click')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    const html = String(emitted!.at(-1)![0])
    expect(html).toContain('alt="new alt"')
    w.unmount()
  })

  // Catches the weaker guard as well as no guard at all. An external model push replaces the
  // document via setContent() while the dialog is open, and ProseMirror maps the NodeSelection
  // onto the REPLACEMENT document's image -- so "is the selection still a NodeSelection on an
  // image" passes and the alt is written to the wrong picture. Only a node-identity comparison
  // makes this test pass.
  it('does not write the alt onto a different image when an external model push replaces the content while the dialog is open', async () => {
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png" alt="old"></p>' },
      global: globalOpts,
    })
    await flushPromises()
    await dispatchContextMenu(w.get('.ProseMirror img').element)
    const menuVm = w.findComponent(RichTextContextMenu).vm as unknown as {
      runImage: (action: ImageAction) => void
    }
    menuVm.runImage('editAlt')
    await flushPromises()
    // The external push, arriving while the dialog sits open.
    await w.setProps({ modelValue: '<p><img src="https://example.com/dog.png" alt="old"></p>' })
    await flushPromises()
    await w.get('[data-testid="alt"]').setValue('new alt')
    await w.get('[data-cmd="altSubmit"]').trigger('click')
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    expect(vm.editor.getHTML()).toContain('src="https://example.com/dog.png"')
    expect(vm.editor.getHTML()).not.toContain('new alt')
    w.unmount()
  })

  // A right-click never blurs a contenteditable, so the editor still holds focus when "edit alt"
  // runs. Pins the synchronous blur, which is the part jsdom can see -- it never raises the
  // aria-hidden console warning the blur exists for. The destination is asserted exactly:
  // "not the editor" would also pass if focus moved somewhere else still inside the hidden subtree.
  it('blurs the editor before the alt dialog opens, so focus is not left behind for an aria-hidden ancestor to catch', async () => {
    const container = attachContainer()
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png" alt="a cat"></p>' },
      global: globalOpts, attachTo: container,
    })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.focus()
    await waitForEditorReactivity()
    expect(document.activeElement).toBe(w.get('.ProseMirror').element)
    await dispatchContextMenu(w.get('.ProseMirror img').element)
    const menuVm = w.findComponent(RichTextContextMenu).vm as unknown as {
      runImage: (action: ImageAction) => void
    }
    menuVm.runImage('editAlt')
    expect(document.activeElement).toBe(document.body)
    w.unmount()
  })

  // Pins the destination and the timing of the restore, NOT the explicit restore itself: this test
  // focuses the editor before opening, so it still passes if onImageAction falls back to the
  // default capture. The test below it, which focuses an element that is then unmounted, is the
  // one that fails without it.
  //
  // The close is simulated with $emit rather than a Cancel-button click: the mechanism under test
  // is RichTextInput's own close-change handler, not that button's wiring.
  it('restores focus to the editor when the alt dialog is cancelled', async () => {
    const container = attachContainer()
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png" alt="a cat"></p>' },
      global: globalOpts, attachTo: container,
    })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.focus()
    await waitForEditorReactivity()
    await dispatchContextMenu(w.get('.ProseMirror img').element)
    const menuVm = w.findComponent(RichTextContextMenu).vm as unknown as {
      runImage: (action: ImageAction) => void
    }
    menuVm.runImage('editAlt')
    expect(document.activeElement).toBe(document.body)
    // runImage() bypasses the real @select interaction, so the context menu is still open and its
    // focus trap would steal back any restore landing outside it. Escape closes it, as a real
    // selection would.
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()
    w.findComponent(RichTextImageAltDialog).vm.$emit('update:open', false)
    // One tick for refocusAfterDialogCancel's own deferral, then a frame for tiptap's own
    // deferred dom.focus().
    await nextTick()
    await waitForEditorReactivity()
    expect(document.activeElement).toBe(w.get('.ProseMirror').element)
    w.unmount()
  })

  // The discriminating half: the element focused when the alt dialog opens is a context-menu item
  // that is gone by the time the cancel runs, so a restore built from the default capture focuses
  // a detached node and leaves focus on <body>. Fails if onImageAction drops its explicit restore.
  it('restores focus to the editor even when the element focused before the alt dialog is unmounted', async () => {
    const container = attachContainer()
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png" alt="a cat"></p>' },
      global: globalOpts, attachTo: container,
    })
    await flushPromises()
    const menuItem = document.body.appendChild(document.createElement('button'))
    menuItem.focus()
    expect(document.activeElement).toBe(menuItem)
    await dispatchContextMenu(w.get('.ProseMirror img').element)
    const menuVm = w.findComponent(RichTextContextMenu).vm as unknown as {
      runImage: (action: ImageAction) => void
    }
    menuVm.runImage('editAlt')
    // Stand-in for reka's own context menu unmounting on its way out.
    menuItem.remove()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()
    w.findComponent(RichTextImageAltDialog).vm.$emit('update:open', false)
    await nextTick()
    await waitForEditorReactivity()
    expect(document.activeElement).toBe(w.get('.ProseMirror').element)
    w.unmount()
  })

  // Fails if onImageAltDialogSubmit stops clearing the captured pre-dialog target. The single
  // nextTick after the submit is exact and load-bearing: long enough for a wrongly-armed restore
  // to have fired its synchronous focus(), too short for tiptap's own deferred dom.focus() to have
  // landed -- so activeElement here can only reflect the stale restore.
  it('does not restore stale pre-dialog focus after a successful alt-dialog submit', async () => {
    const container = attachContainer()
    const w = mount(RichTextInput, {
      props: { modelValue: '<p><img src="https://example.com/cat.png" alt="old"></p>' },
      global: globalOpts, attachTo: container,
    })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.focus()
    await waitForEditorReactivity()
    const editorEl = w.get('.ProseMirror').element
    await dispatchContextMenu(w.get('.ProseMirror img').element)
    const menuVm = w.findComponent(RichTextContextMenu).vm as unknown as {
      runImage: (action: ImageAction) => void
    }
    menuVm.runImage('editAlt')
    expect(document.activeElement).toBe(document.body)
    // Same as the cancel test above: close the still-open context menu so its focus trap does not
    // fight the restore under test.
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()
    await flushPromises()
    await w.get('[data-testid="alt"]').setValue('new alt')
    await w.get('[data-cmd="altSubmit"]').trigger('click')
    await nextTick()
    expect(document.activeElement).not.toBe(editorEl)
    w.unmount()
  })

  // The table-size dialog reaches the same helpers through one more layer (RichTextTableMenu's
  // popover); this pins the same synchronous destination for that entry point.
  it('blurs the editor before the table-size dialog opens', async () => {
    const container = attachContainer()
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts, attachTo: container })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.focus()
    await waitForEditorReactivity()
    expect(document.activeElement).toBe(w.get('.ProseMirror').element)
    await w.get('[data-cmd="table"]').trigger('click')
    const clicked = w.get('[data-cmd="tableCustomSize"]').trigger('click')
    expect(document.activeElement).toBe(document.body)
    await clicked
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

  // The server wraps header rows in <thead>, so that is what arrives in modelValue.
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
    // ...and TipTap re-serializes without the thead, which is exactly why that normalization lives
    // on the server and why the editor needs its own tbody-th styling rule.
    expect(html).not.toContain('<thead')
  })

  // BubbleMenuPlugin's own update() (registered by RichTextBubbleMenu, mounted in RichTextInput's
  // template) dispatches through window.setTimeout at updateDelay's default of 250ms whenever the
  // selection is non-collapsed -- selectAll() below produces exactly that case, so a synchronous
  // assertion right after selecting/focusing would still see the pre-selection (hidden) state.
  // Two more deferrals sit in front of that one: tiptap's focus command defers view.focus() into a
  // requestAnimationFrame (@tiptap/core's focus.ts), and the plugin's own focusHandler then
  // schedules a zero-delay setTimeout, so ~266ms of chained deferrals gate the assertion.
  //
  // Advanced on vitest's fake clock, not waited out on the real one: a real-clock wait over that
  // chain is only ever a margin, and a margin is a race a loaded machine can win. Virtual time
  // removes the margin instead of widening it. Same approach as RichTextBubbleMenu.test.ts, which
  // states the rest of the constraint. The clock is installed per test here, not file-wide as it
  // is there, because the fourteen waitForEditorReactivity() call sites and three
  // waitForEditorCreate() ones elsewhere in this file AWAIT a real frame or timer rather than
  // advancing one, and a fake clock would strand every one of them.
  const BUBBLE_MENU_SETTLE_MS = 300

  // Call as the first statement of a test, before mount: the fake clock has to be in place before
  // the editor schedules any of the deferrals above, or they stay on the real clock,
  // settleBubbleMenu() advances nothing, and the test is silently vacuous. The attached-host
  // assertion is what enforces that ordering rather than merely documenting it -- every mount in
  // this describe that the bubble menu needs goes through attachContainer(). vi.useRealTimers()
  // runs in this describe's afterEach, so the clock is handed back even when an assertion throws.
  function useBubbleMenuClock(): void {
    expect(attachedContainers).toHaveLength(0)
    vi.useFakeTimers()
  }

  async function settleBubbleMenu(): Promise<void> {
    await vi.advanceTimersByTimeAsync(BUBBLE_MENU_SETTLE_MS)
    await flushPromises()
  }

  // The fake-clock counterpart of waitForEditorReactivity, for the clocked tests above: same two
  // animation frames, advanced rather than awaited, since useBubbleMenuClock() put
  // requestAnimationFrame on the virtual clock. Exactly two, not a blanket advance -- tiptap's
  // reactive editor state is a customRef whose trigger() sits behind a nested double
  // requestAnimationFrame (@tiptap/vue-3's useDebouncedRef), and an advance wider than that would
  // stop discriminating a regression that stretched it.
  async function advanceEditorReactivity(): Promise<void> {
    vi.advanceTimersToNextFrame()
    vi.advanceTimersToNextFrame()
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
    useBubbleMenuClock()
    const container = attachContainer()
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
  })

  // The toolbar and the bubble menu render the same data-cmd values while the menu is
  // open. Neither component's own test can see the two coexisting -- RichTextBubbleMenu.test.ts
  // mounts no toolbar, and the toolbar tests above never open the bubble menu -- so a wiring
  // mistake that fires a command through both surfaces for one click (toggling bold back off), or
  // that leaves the toolbar's own reactive state stale after a command run through the OTHER
  // surface, is invisible anywhere else in this suite.
  it('does not double-fire a command shared with the toolbar, and the toolbar reflects a change made through the menu', async () => {
    useBubbleMenuClock()
    const container = attachContainer()
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
    // advanceEditorReactivity, not waitForEditorReactivity: same two frames, but this test's clock
    // is virtual, so they have to be advanced rather than awaited.
    await advanceEditorReactivity()
    expect(w.get('.rich-text__toolbar [data-cmd="bold"]').attributes('data-active')).toBe('true')

    w.unmount()
  })

  // BubbleMenuPlugin arms `preventHide` on its own mousedown, which swallows the very next blur, so
  // the dialog's autofocus stealing DOM focus from the editor would not, on its own, hide this
  // menu -- it would linger beside the open dialog.
  // A bare VTU `.trigger('click')` dispatches only a 'click' event, no 'mousedown' -- so it does
  // NOT arm preventHide, and the resulting blur hides the menu on its own regardless of whether
  // openLinkDialog's hide() call exists at all (confirmed: deleting that call left this test green).
  // Dispatching 'mousedown' first, as a real click does, is what arms preventHide and makes the
  // blur path a no-op -- only then does reaching this assertion prove hide() (and the matching
  // pluginKey string on both ends) is doing the work.
  it('hides the bubble menu when its own link button opens the dialog', async () => {
    useBubbleMenuClock()
    const container = attachContainer()
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
  })

  // The bubble menu's link button is unmounted by hide() before the cancel runs, so this pins
  // focusIfStillInDocument's editor fallback. The toolbar's link button shares openLinkDialog and
  // must still restore to itself (tested below), so the fallback must not fire unconditionally.
  //
  // Button is un-stubbed here: the shared stub renders an HTMLUnknownElement, which jsdom will not
  // focus, so the button could never be the captured activeElement and the test would not
  // discriminate.
  it('restores focus to the editor when the link dialog opened from the bubble menu is cancelled', async () => {
    useBubbleMenuClock()
    const container = attachContainer()
    const w = mount(RichTextInput, {
      props: { modelValue: '<p>abc</p>' },
      global: { ...globalOpts, stubs: { ...stubs, Button: false } },
      attachTo: container,
    })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.selectAll()
    vm.editor.commands.focus()
    await settleBubbleMenu()
    const linkBtn = bubbleRoot().get('[data-cmd="link"]').element as HTMLElement
    // Focused explicitly: jsdom's synthetic click does not reproduce a native button's
    // focus-follows-click default action.
    linkBtn.focus()
    expect(document.activeElement).toBe(linkBtn)
    linkBtn.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }))
    linkBtn.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()
    expect(document.body.contains(linkBtn)).toBe(false)
    w.findComponent(RichTextLinkDialog).vm.$emit('update:open', false)
    await nextTick()
    // advanceEditorReactivity, not waitForEditorReactivity: see the note in the double-fire test
    // above -- requestAnimationFrame is on the virtual clock here, so it is advanced, not awaited.
    await advanceEditorReactivity()
    expect(document.activeElement).toBe(w.get('.ProseMirror').element)
    w.unmount()
  })

  // The link-dialog half of the same blur. jsdom's synthetic click does not reproduce a native
  // button's focus-follows-click, so focus starts on the editor here: what this pins is that the
  // blur moves focus off WHATEVER held it. The destination is asserted exactly, not as "not the
  // editor", which would also pass for focus landing elsewhere inside the hidden subtree.
  it('blurs the editor before the link dialog opens, so focus is not left behind for an aria-hidden ancestor to catch', async () => {
    const container = attachContainer()
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts, attachTo: container })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.focus()
    await waitForEditorReactivity()
    expect(document.activeElement).toBe(w.get('.ProseMirror').element)
    // Asserted BEFORE awaiting the click's settle promise, and that is what makes it discriminate:
    // reka's own autofocus resolves ahead of a nextTick awaited from out here, so an awaited
    // assertion would pass with the blur removed.
    const clicked = w.get('[data-cmd="link"]').trigger('click')
    expect(document.activeElement).toBe(document.body)
    await clicked
    w.unmount()
  })

  // The restore half. reka cannot do this itself once focus is on <body>, so onLinkDialogOpenChange
  // is the only thing putting focus back. Closed via $emit, matching an Escape or overlay click,
  // because the mechanism under test is that handler and not any button's wiring.
  it('restores focus to whatever held it before the link dialog opened, when the dialog is cancelled', async () => {
    const container = attachContainer()
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts, attachTo: container })
    await flushPromises()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.focus()
    await waitForEditorReactivity()
    const editorEl = w.get('.ProseMirror').element
    // Awaited here, unlike the blur test above: this pins the restore, so reka's own autofocus
    // has to settle first rather than be raced.
    await w.get('[data-cmd="link"]').trigger('click')
    await flushPromises()
    w.findComponent(RichTextLinkDialog).vm.$emit('update:open', false)
    // One tick for refocusAfterDialogCancel's own deferral.
    await nextTick()
    expect(document.activeElement).toBe(editorEl)
    w.unmount()
  })

  // The table-size dialog's own half. Its pre-dialog focus is the "Custom size..." entry, which
  // unmounts with the popover, and that popover's own restore is suppressed on this path -- so the
  // trigger RichTextTableMenu hands over is the only remaining target.
  //
  // Button is un-stubbed here: the shared stub renders an HTMLUnknownElement, which jsdom will not
  // focus, so the test would fail for a reason unrelated to the code under test.
  it('restores focus to the table toolbar button when the table-size dialog is cancelled', async () => {
    const container = attachContainer()
    const w = mount(RichTextInput, {
      props: { modelValue: '<p>a</p>' },
      global: { ...globalOpts, stubs: { ...stubs, Button: false } },
      attachTo: container,
    })
    await flushPromises()
    const trigger = w.get('[data-cmd="table"]').element
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="tableCustomSize"]').trigger('click')
    await flushPromises()
    w.findComponent(RichTextTableSizeDialog).vm.$emit('update:open', false)
    await nextTick()
    expect(document.activeElement).toBe(trigger)
    w.unmount()
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
    const container = attachContainer()
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: globalOpts, attachTo: container })
    await flushPromises()
    await w.get('[data-cmd="image"]').trigger('click')
    await flushPromises()
    const messages = warn.mock.calls.map((c) => c.map(String).join(' '))
    expect(messages.filter((m) => m.includes('Missing `Description`'))).toEqual([])
    w.unmount()
  })

  // ---- slash commands -------------------------------------------------------------------
  //
  // The menu is not part of the mounted tree: @tiptap/suggestion's props.mount() appends it to
  // document.body, so every query below goes through `document`, and it is reachable even though
  // most of these mount detached. The editable, by contrast, is only reachable through the
  // wrapper for a detached mount -- hence slashKey() taking one.

  async function mountSlash(props: Record<string, unknown> = {}): Promise<VueWrapper> {
    const w = mount(RichTextInput, { props: { modelValue: '<p></p>', ...props }, global: globalOpts })
    await waitForEditorCreate()
    return w
  }

  function editorOf(w: VueWrapper): Editor {
    return (w.vm as unknown as { editor: Editor }).editor
  }

  function editableOf(w: VueWrapper): Element {
    return w.get('.ProseMirror').element
  }

  async function openSlash(editor: Editor, query = ''): Promise<void> {
    editor.commands.insertContent(`/${query}`)
    // The plugin's view awaits its own items() call before dispatching the update that carries the
    // list, so the options are two microtask turns behind the transaction that opened the menu --
    // a single nextTick() sees the menu but no options in it.
    await flushPromises()
  }

  function slashMenu(): HTMLElement | null {
    return document.querySelector('[role="listbox"]')
  }

  function slashOptions(): HTMLElement[] {
    return Array.from(document.querySelectorAll('[role="option"]'))
  }

  function activeOption(): HTMLElement | null {
    return document.querySelector('[role="option"][aria-selected="true"]')
  }

  // jsdom performs no layout -- getBoundingClientRect, clientTop and clientHeight all read 0 --
  // so the menu's geometry is supplied here. scrollTop is the one piece that is real: jsdom stores
  // what is written to it, which is what makes the assertion below possible. The numbers are the
  // ones a browser reports for this menu: 32px rows, eight of the twelve fully visible.
  //
  // The 1px border is modelled rather than zeroed: the rect a browser returns is the BORDER box,
  // while scrollTop and clientHeight describe the content box inside it. Setting clientTop to 0 and
  // the rect height equal to clientHeight would make both of those terms inert here while they stay
  // load-bearing in a browser.
  const SLASH_ROW = 32
  const SLASH_VISIBLE_ROWS = 8
  const SLASH_BORDER = 1
  const SLASH_CONTENT_HEIGHT = SLASH_ROW * SLASH_VISIBLE_ROWS

  function fakeRect(top: number, height: number): DOMRect {
    return {
      top, bottom: top + height, height, y: top, left: 0, right: 0, width: 0, x: 0,
      toJSON: () => undefined,
    } as unknown as DOMRect
  }

  function layOutSlashMenu(menu: HTMLElement, options: HTMLElement[]): void {
    Object.defineProperty(menu, 'clientTop', { configurable: true, value: SLASH_BORDER })
    Object.defineProperty(menu, 'clientHeight', { configurable: true, value: SLASH_CONTENT_HEIGHT })
    menu.getBoundingClientRect = () => fakeRect(0, SLASH_CONTENT_HEIGHT + SLASH_BORDER * 2)
    options.forEach((option, i) => {
      option.getBoundingClientRect = () => (
        fakeRect(SLASH_BORDER + i * SLASH_ROW - menu.scrollTop, SLASH_ROW)
      )
    })
  }

  function slashKey(w: VueWrapper, key: string): boolean {
    const ev = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })
    editableOf(w).dispatchEvent(ev)
    return ev.defaultPrevented
  }

  it('opens the slash menu when a slash is typed', async () => {
    const w = await mountSlash()
    await openSlash(editorOf(w))
    expect(slashMenu()).not.toBeNull()
    expect(slashOptions().length).toBeGreaterThan(0)
  })

  it('does not open the menu for a slash inside a word', async () => {
    const w = await mountSlash()
    editorOf(w).commands.insertContent('and/or')
    await flushPromises()
    // Asserted first, and on the text rather than the markup: without it this passes for the wrong
    // reason the moment the slash stops reaching the document at all.
    expect(editorOf(w).getText()).toBe('and/or')
    expect(slashMenu()).toBeNull()
  })

  it('opens the menu for a slash typed after a space', async () => {
    const w = await mountSlash()
    const editor = editorOf(w)
    editor.commands.insertContent('see ')
    await openSlash(editor)
    expect(editor.getText()).toBe('see /')
    expect(slashMenu()).not.toBeNull()
  })

  // The two cases below are the reason the extension measures the character before the slash
  // against the BLOCK rather than trusting upstream's text-node-scoped prefix check alone. Both
  // put the slash at offset 0 of a brand-new text node, where upstream finds no character to
  // reject; only a block-scoped check sees the letter that is plainly there on screen.

  // Link reaches this in production with no setup: it is configured non-inclusive
  // (Link.configure({ autolink: false }) in RichTextInput.vue, and extension-link's own
  // inclusive() returns that option verbatim), so the slash never joins the link's text node.
  it('does not open the menu for a slash typed immediately after a link', async () => {
    const w = await mountSlash({ modelValue: '<p><a href="https://example.com">read more</a></p>' })
    const editor = editorOf(w)
    editor.commands.focus('end')
    await openSlash(editor)
    expect(editor.getHTML()).toBe('<p><a href="https://example.com">read more</a>/</p>')
    expect(slashMenu()).toBeNull()
  })

  // Bold overrides nothing and so is inclusive by ProseMirror's default: the slash would join the
  // bold text node and be rejected on the same character either way. Clearing the mark first is
  // what forces the fresh text node, making this the same shape as the link case by hand.
  it('does not open the menu for a slash typed after text whose mark was just cleared', async () => {
    const w = await mountSlash({ modelValue: '<p><strong>bold</strong></p>' })
    const editor = editorOf(w)
    editor.commands.focus('end')
    editor.commands.unsetMark('bold')
    await openSlash(editor)
    expect(editor.getHTML()).toBe('<p><strong>bold</strong>/</p>')
    expect(slashMenu()).toBeNull()
  })

  it('does not open the menu inside a code block', async () => {
    const w = await mountSlash()
    const editor = editorOf(w)
    editor.commands.setCodeBlock()
    await openSlash(editor)
    expect(editor.getHTML()).toContain('<code>/</code>')
    expect(slashMenu()).toBeNull()
  })

  // Nothing in this repository enforces this. @tiptap/suggestion's apply() gates its whole
  // match-and-allow block on editor.isEditable, so the plugin never gets far enough to consider
  // opening -- which is why the extension carries no isEditable clause of its own. Kept as a
  // tripwire on that upstream guarantee, to fire when a TipTap family bump comes up for review.
  it('does not open the menu when the field is read-only', async () => {
    const w = await mountSlash({ disabled: true })
    await flushPromises()
    const editor = editorOf(w)
    expect(editor.isEditable).toBe(false)
    await openSlash(editor)
    expect(editor.getText()).toBe('/')
    expect(slashMenu()).toBeNull()
  })

  it('narrows the list as the query is typed', async () => {
    const w = await mountSlash()
    await openSlash(editorOf(w), 'h3')
    expect(slashOptions().map((o) => o.textContent?.trim())).toEqual(['Heading 3'])
  })

  // The locale flips AFTER the editor is built, which is the point: an extension is constructed
  // once, so a list built at construction time would still be the English one here.
  it('finds an item by its ascii alias under a translated locale', async () => {
    const w = await mountSlash()
    i18n.global.locale.value = 'zh-TW'
    await nextTick()
    await openSlash(editorOf(w), 'table')
    expect(slashOptions().map((o) => o.textContent?.trim())).toEqual(['表格'])
  })

  it('shows the empty state without closing, and drops the stale option reference', async () => {
    const w = await mountSlash()
    const editor = editorOf(w)
    await openSlash(editor, 'h')
    expect(editableOf(w).hasAttribute('aria-activedescendant')).toBe(true)
    // Narrowed to nothing rather than opened on nothing: dropping the reference is only a
    // transition the code can get wrong once there is a reference to drop.
    editor.commands.insertContent('zzzz')
    await flushPromises()
    expect(slashMenu()).not.toBeNull()
    expect(slashOptions()).toHaveLength(0)
    expect(slashMenu()?.textContent).toContain('No matching commands')
    expect(editableOf(w).hasAttribute('aria-activedescendant')).toBe(false)
    // aria-owns is not the option reference and must NOT come off here: the menu is still open,
    // and the empty state it is showing is what the owning reference makes reachable.
    expect(editableOf(w).hasAttribute('aria-owns')).toBe(true)
  })

  it('moves the selection with the arrow keys and wraps', async () => {
    const w = await mountSlash()
    await openSlash(editorOf(w))
    const labels = slashOptions().map((o) => o.textContent?.trim())
    expect(activeOption()?.textContent?.trim()).toBe(labels[0])

    expect(slashKey(w, 'ArrowDown')).toBe(true)
    expect(activeOption()?.textContent?.trim()).toBe(labels[1])

    expect(slashKey(w, 'ArrowUp')).toBe(true)
    expect(slashKey(w, 'ArrowUp')).toBe(true)
    expect(activeOption()?.textContent?.trim()).toBe(labels[labels.length - 1])

    expect(slashKey(w, 'ArrowDown')).toBe(true)
    expect(activeOption()?.textContent?.trim()).toBe(labels[0])
  })

  // A tripwire on upstream, not a specification of ours. @tiptap/suggestion dispatches an
  // intermediate update carrying items: [] before it awaits items(), so onUpdate's
  // `selected >= items.length` guard fires on every keystroke and the selection returns to the top
  // -- which is the behaviour a person expects, arrived at by accident. This fails if upstream ever
  // skips that loading pass, or if anyone here sets initialItems or minQueryLength. The failure it
  // guards is visible rather than silent (both data-active and aria-activedescendant follow the
  // index, so a stale highlight is on screen), which is why a test is the whole remedy.
  it('puts the selection back on the first option when the query changes', async () => {
    const w = await mountSlash()
    const editor = editorOf(w)
    await openSlash(editor)
    slashKey(w, 'ArrowDown')
    slashKey(w, 'ArrowDown')
    slashKey(w, 'ArrowDown')
    expect(activeOption()?.textContent?.trim()).toBe('Heading 5')

    editor.commands.insertContent('h')
    await flushPromises()
    // The narrowed list has to stay longer than the index we moved to, or the length guard would
    // reset the selection on its own and this would prove nothing.
    expect(slashOptions().length).toBeGreaterThan(3)
    expect(activeOption()?.textContent?.trim()).toBe('Heading 2')
  })

  it('runs the selected item on Enter and removes the typed query', async () => {
    const w = await mountSlash()
    const editor = editorOf(w)
    await openSlash(editor, 'quote')
    expect(slashKey(w, 'Enter')).toBe(true)
    await flushPromises()
    expect(editor.getHTML()).toContain('<blockquote>')
    // Both halves are required, and a wrapping command is what makes the second one bite: running
    // the item before the delete shifts every position after the wrapper's opening tokens, so the
    // range stops covering the text it was measured against and part of "/quote" survives inside
    // the new block. Asserted on the text rather than the markup, which carries the query's own
    // letters inside its tags either way.
    expect(editor.getText().trim()).toBe('')
    expect(slashMenu()).toBeNull()
  })

  it('closes on Escape', async () => {
    const w = await mountSlash()
    await openSlash(editorOf(w))
    expect(slashKey(w, 'Escape')).toBe(true)
    await flushPromises()
    expect(slashMenu()).toBeNull()
  })

  // Not intercepted, so the browser moves focus itself -- and the menu then goes away with the
  // blur that follows, which a dispatched keydown does not produce here. The blur test below is
  // what covers the second half.
  it('leaves Tab to the form, so the field keeps its place in the tab order', async () => {
    const w = await mountSlash()
    await openSlash(editorOf(w))
    expect(slashKey(w, 'Tab')).toBe(false)
  })

  it('points aria-activedescendant at the selected option', async () => {
    const w = await mountSlash()
    await openSlash(editorOf(w), 'h')
    const editable = editableOf(w)
    expect(editable.getAttribute('aria-activedescendant')).toBe(activeOption()?.id)
    // Half of the cross-task id contract: RichTextSlashMenu.vue builds the same string from the
    // same prefix and the item's own id, and pins it from its own side.
    expect(activeOption()?.id.endsWith('-slash-heading2')).toBe(true)

    slashKey(w, 'ArrowDown')
    expect(editable.getAttribute('aria-activedescendant')).toBe(activeOption()?.id)
    expect(activeOption()?.id.endsWith('-slash-heading3')).toBe(true)
  })

  // aria-activedescendant on its own names an element that is neither a DOM descendant of the
  // editable nor owned by it, and a reference like that is not required to resolve at all. This
  // is the assertion that the announcement has somewhere to come from.
  it('owns the mounted menu from the editable, so the referenced option is a logical descendant', async () => {
    const w = await mountSlash()
    await openSlash(editorOf(w))
    const editable = editableOf(w)
    const menu = slashMenu()
    expect(menu).not.toBeNull()
    expect(menu?.id).toBeTruthy()
    expect(editable.getAttribute('aria-owns')).toBe(menu?.id)
    expect(menu?.parentElement).toBe(document.body)
    expect(editable.contains(menu)).toBe(false)
    const referenced = document.getElementById(editable.getAttribute('aria-activedescendant') ?? '')
    expect(menu?.contains(referenced)).toBe(true)

    // Attributes written straight onto view.dom survive ProseMirror's own attribute patching only
    // because it removes just the ones a previous decoration put there. Typing another character
    // is the cheapest way to keep that true.
    editorOf(w).commands.insertContent('h')
    await flushPromises()
    expect(editable.getAttribute('aria-owns')).toBe(menu?.id)
  })

  it('clears aria-activedescendant and aria-owns when the menu closes', async () => {
    const w = await mountSlash()
    const editable = editableOf(w)
    await openSlash(editorOf(w))
    expect(editable.hasAttribute('aria-activedescendant')).toBe(true)
    expect(editable.hasAttribute('aria-owns')).toBe(true)

    slashKey(w, 'Escape')
    await flushPromises()
    expect(editable.hasAttribute('aria-activedescendant')).toBe(false)
    expect(editable.hasAttribute('aria-owns')).toBe(false)
  })

  // The two tests below are the only thing joining the extension's onSelect/onHover props to the
  // component's select/hover emits. Each side pins its own half; rename either prop and the whole
  // mouse path dies without a single keyboard test noticing.
  it('runs the item under the pointer on mousedown', async () => {
    const w = await mountSlash()
    const editor = editorOf(w)
    await openSlash(editor)
    const third = slashOptions()[2]
    expect(third.textContent?.trim()).toBe('Heading 4')
    const mousedown = new MouseEvent('mousedown', { bubbles: true, cancelable: true })
    third.dispatchEvent(mousedown)
    // The row's own handler unmounts the menu before the event reaches the root that cancels the
    // default -- which still runs, because the propagation path is fixed when dispatch begins.
    // Without the cancel the editor blurs, and a blur now closes the menu and drops the query.
    expect(mousedown.defaultPrevented).toBe(true)
    await flushPromises()
    expect(editor.getHTML()).toContain('<h4')
    expect(editor.getText().trim()).toBe('')
    expect(slashMenu()).toBeNull()
  })

  it('moves the selection to the option under the pointer', async () => {
    const w = await mountSlash()
    await openSlash(editorOf(w))
    // Hover counts only after real pointer movement -- see the test after this one.
    document.dispatchEvent(new Event('pointermove'))
    const fourth = slashOptions()[3]
    // mouseenter does not bubble; dispatched on the element the listener is bound to.
    fourth.dispatchEvent(new MouseEvent('mouseenter'))
    expect(activeOption()).toBe(fourth)
    expect(editableOf(w).getAttribute('aria-activedescendant')).toBe(fourth.id)
  })

  // A cursor left resting over the list must not steal the arrow keys. In a browser the rows
  // themselves move under the stationary pointer once the list scrolls, the browser re-evaluates
  // hover, and mouseenter arrives with no pointer movement behind it.
  it('ignores hover that no pointer movement caused', async () => {
    const w = await mountSlash()
    await openSlash(editorOf(w))
    document.dispatchEvent(new Event('pointermove'))
    slashKey(w, 'ArrowDown')
    const chosenByKeyboard = activeOption()
    expect(chosenByKeyboard?.textContent?.trim()).toBe('Heading 3')

    slashOptions()[7].dispatchEvent(new MouseEvent('mouseenter'))
    expect(activeOption()).toBe(chosenByKeyboard)
  })

  // The next two tests exist because onExit's two teardown calls are mutually redundant as far as
  // the DOM is concerned -- either one alone removes the menu, so "the menu is gone" is no evidence
  // that both ran, and the resources they release are different.
  //
  // This one covers props.mount()'s returned function: upstream registers a capture-phase
  // pointerdown listener on document for dismissOnOutsideClick, and nothing else takes it off.
  it('removes the outside-click listener when the menu closes', async () => {
    const w = await mountSlash()
    const added = vi.spyOn(document, 'addEventListener')
    const removed = vi.spyOn(document, 'removeEventListener')
    await openSlash(editorOf(w))
    expect(added.mock.calls.filter((c) => c[0] === 'pointerdown' && c[2] === true)).toHaveLength(1)
    expect(removed.mock.calls.filter((c) => c[0] === 'pointerdown' && c[2] === true)).toHaveLength(0)

    slashKey(w, 'Escape')
    await flushPromises()
    expect(removed.mock.calls.filter((c) => c[0] === 'pointerdown' && c[2] === true)).toHaveLength(1)
  })

  // The pointermove and blur listeners this extension adds itself. Neither removal changes anything
  // observable about the menu, so nothing else here can catch a missing one -- and a surviving blur
  // handler would dispatch an exit transaction on every later blur of that editor for its whole
  // life, on an element it merely assumes is still the same node.
  it('releases the pointer and blur listeners it added when the menu closes', async () => {
    const w = await mountSlash()
    const editable = editableOf(w)
    const docAdd = vi.spyOn(document, 'addEventListener')
    const docRemove = vi.spyOn(document, 'removeEventListener')
    const editableAdd = vi.spyOn(editable, 'addEventListener')
    const editableRemove = vi.spyOn(editable, 'removeEventListener')

    await openSlash(editorOf(w))
    expect(docAdd.mock.calls.filter((c) => c[0] === 'pointermove')).toHaveLength(1)
    expect(editableAdd.mock.calls.filter((c) => c[0] === 'blur')).toHaveLength(1)
    expect(docRemove.mock.calls.filter((c) => c[0] === 'pointermove')).toHaveLength(0)
    expect(editableRemove.mock.calls.filter((c) => c[0] === 'blur')).toHaveLength(0)

    slashKey(w, 'Escape')
    await flushPromises()
    expect(docRemove.mock.calls.filter((c) => c[0] === 'pointermove')).toHaveLength(1)
    expect(editableRemove.mock.calls.filter((c) => c[0] === 'blur')).toHaveLength(1)
  })

  // Escape is an explicit "no" and upstream makes it stick. A blur is not a "no": Tab, a click into
  // another field and, in Chrome, the window itself losing focus all produce one, and none of them
  // should cost the writer the query they had half typed.
  it('reopens on the same query after the editor was blurred', async () => {
    const w = await mountSlash()
    const editor = editorOf(w)
    await openSlash(editor, 'ta')
    editableOf(w).dispatchEvent(new FocusEvent('blur'))
    await flushPromises()
    expect(slashMenu()).toBeNull()

    editor.commands.insertContent('b')
    await flushPromises()
    expect(slashMenu()).not.toBeNull()
  })

  it('stays dismissed on the same query after Escape', async () => {
    const w = await mountSlash()
    const editor = editorOf(w)
    await openSlash(editor, 'ta')
    slashKey(w, 'Escape')
    await flushPromises()
    expect(slashMenu()).toBeNull()

    editor.commands.insertContent('b')
    await flushPromises()
    expect(slashMenu()).toBeNull()
  })

  // The pairing, not just the flag: whatever the last exit was has to be re-derived on every
  // deactivation, so that a blur earlier in the session cannot make a later Escape reopen.
  it('does not let an earlier blur make a later Escape reopen the menu', async () => {
    const w = await mountSlash()
    const editor = editorOf(w)
    await openSlash(editor, 'ta')
    editableOf(w).dispatchEvent(new FocusEvent('blur'))
    await flushPromises()
    editor.commands.insertContent('b')
    await flushPromises()
    expect(slashMenu()).not.toBeNull()

    slashKey(w, 'Escape')
    await flushPromises()
    editor.commands.insertContent('l')
    await flushPromises()
    expect(slashMenu()).toBeNull()
  })

  // ...and this one covers the other call: nothing else releases the Vue component instance.
  it('destroys the menu renderer when the menu closes', async () => {
    const w = await mountSlash()
    await openSlash(editorOf(w))
    const destroy = vi.spyOn(VueRenderer.prototype, 'destroy')
    slashKey(w, 'Escape')
    await flushPromises()
    expect(destroy).toHaveBeenCalledTimes(1)
  })

  // The menu is its own scroll container, and it is the ONLY thing that may scroll. Element
  // .scrollIntoView walks every scrollable ancestor up to the document, and the first sync() of an
  // open runs before floating-ui has positioned the menu -- so it scrolled the page to its maximum
  // and took the editor and the menu off-screen with it.
  it('scrolls the menu itself past the fold, and never the page', async () => {
    const w = await mountSlash()
    const pageScroll = vi.spyOn(Element.prototype, 'scrollIntoView')
    await openSlash(editorOf(w))
    const menu = slashMenu()
    expect(menu).not.toBeNull()
    layOutSlashMenu(menu as HTMLElement, slashOptions())

    for (let i = 0; i < SLASH_VISIBLE_ROWS - 1; i += 1) slashKey(w, 'ArrowDown')
    expect(activeOption()).toBe(slashOptions()[SLASH_VISIBLE_ROWS - 1])
    expect(menu?.scrollTop).toBe(0)

    slashKey(w, 'ArrowDown')
    expect(menu?.scrollTop).toBe(SLASH_ROW)
    slashKey(w, 'ArrowDown')
    expect(menu?.scrollTop).toBe(SLASH_ROW * 2)

    expect(pageScroll).not.toHaveBeenCalled()
  })

  // A blur dispatches no transaction, so nothing in the suggestion plugin's own state would
  // otherwise notice focus leaving -- without this, tabbing away, or any route out other than a
  // click outside, would leave the menu on screen with an unfocused editable still advertising it.
  it('closes the menu and drops both ARIA references when the editor loses focus', async () => {
    const w = await mountSlash()
    const editable = editableOf(w)
    await openSlash(editorOf(w))
    expect(slashMenu()).not.toBeNull()

    editable.dispatchEvent(new FocusEvent('blur'))
    await flushPromises()
    expect(slashMenu()).toBeNull()
    expect(editable.hasAttribute('aria-activedescendant')).toBe(false)
    expect(editable.hasAttribute('aria-owns')).toBe(false)
  })

  // Extension.configure() takes a Partial, so nothing at the type level stops a fork from mounting
  // this without a command context.
  it('fails by name when the extension is used without a command context', () => {
    expect(() => RichTextSlashExtension.options.context.openImageDialog())
      .toThrow(/configure\(\{ context \}\)/)
  })
})

// The resize handles are positioned and revealed entirely by CSS, jsdom performs no layout, and
// this component's scoped styles are never injected in the test environment (document.styleSheets
// is empty when it mounts), so nothing here can measure where a handle lands or whether a person
// could see it. Two things ARE decidable without layout, and this suite pins both:
//   - which of the stylesheet's rules SELECT a given handle in a given editor state, answered by
//     running the component's own selectors back over the real mounted DOM with element.matches();
//   - that the declarations doing the positioning are present and on the axis they have to be on.
// Whether the handles then look right, and whether a drag from one resizes the image, needs a real
// browser.
//
// The regressions these exist for both shipped. Upstream writes both ends of an edge handle's long
// axis as INLINE styles (left:0 and right:0 for top/bottom), which over-constrains a fixed-size
// box, so the browser drops one end and the handle collapses onto a corner -- the feature looked
// complete because eight handle ELEMENTS existed while only four positions were reachable. And
// upstream attaches the handles from the node view's constructor and removes them only when the
// editor stops being editable, so an image nobody has selected still carried eight of them.
// ?raw so the assertions read the file's own text: the compiled component carries no styles here.
// The slice starts after the FIRST <style scoped> and stops at </style>, and comments are stripped,
// because the template above and the block's own prose both use words like `auto` and `display` --
// any of which would otherwise decide these assertions instead of the declarations doing.
const STYLE_OPEN = '<style scoped>'
const styleBlock = richTextInputSource
  .slice(richTextInputSource.indexOf(STYLE_OPEN) + STYLE_OPEN.length)
  .split('</style>')[0]
  .replace(/\/\*[\s\S]*?\*\//g, '')

type StyleRule = { selectors: string[]; body: string }

// Sound only because this block contains no at-rules and no nesting; both would need a real parser.
const styleRules: StyleRule[] = styleBlock
  .split('}')
  .map((chunk) => chunk.split('{'))
  .filter((parts) => parts.length === 2)
  .map(([selector, body]) => ({
    selectors: selector.split(',').map((s) => runtimeSelector(s.trim())).filter(Boolean),
    body: body.trim(),
  }))

// `A :deep(B)` compiles to `A[data-v-hash] B`. The scope attribute never reaches the test build, so
// dropping the `:deep(...)` wrapper leaves exactly the selector the browser would match on. Written
// as a paren-counting scan rather than a regex because the hide rule's argument contains `:not(…)`.
function runtimeSelector(selector: string): string {
  const marker = ':deep('
  let out = ''
  let i = 0
  for (;;) {
    const at = selector.indexOf(marker, i)
    if (at === -1) return out + selector.slice(i)
    out += selector.slice(i, at)
    let depth = 1
    let j = at + marker.length
    const start = j
    for (; j < selector.length && depth > 0; j++) {
      if (selector[j] === '(') depth += 1
      else if (selector[j] === ')') depth -= 1
    }
    out += selector.slice(start, j - 1)
    i = j
  }
}

/** Every `display` value the component's own stylesheet would apply to `el`, in source order. */
function displayValuesFor(el: Element): string[] {
  return styleRules
    .filter((rule) => rule.selectors.some((s) => el.matches(s)))
    .map((rule) => /display\s*:\s*([^;]+)/.exec(rule.body)?.[1].trim())
    .filter((value): value is string => value !== undefined)
}

/** The one rule block whose selector list targets exactly this handle direction. */
function declarationsFor(direction: string): string {
  const rule = styleRules.find((r) =>
    r.selectors.some((s) => s.endsWith(`[data-resize-handle="${direction}"]`)),
  )
  return rule?.body ?? ''
}

async function mountWithImage(): Promise<VueWrapper> {
  const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: globalOpts })
  await flushPromises()
  const vm = w.vm as unknown as { editor: Editor }
  vm.editor.commands.setImage({ src: 'https://example.com/cat.png' })
  await flushPromises()
  return w
}

function selectImageNode(w: VueWrapper): void {
  const vm = w.vm as unknown as { editor: Editor }
  const img = w.get('[data-resize-wrapper] img').element
  vm.editor.commands.setNodeSelection(vm.editor.view.posAtDOM(img, 0))
}

describe('RichTextInput resize handle visibility contract', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('hides every handle while the image is not the selection', async () => {
    const w = await mountWithImage()
    const vm = w.vm as unknown as { editor: Editor }
    vm.editor.commands.setTextSelection(1)
    await flushPromises()

    // The precondition and the consequence, in that order: without the first assertion the second
    // would also pass if the class simply never appeared anywhere.
    expect(w.get('[data-resize-container]').classes()).not.toContain('ProseMirror-selectednode')
    const handles = w.findAll('[data-resize-handle]')
    expect(handles.length).toBe(8)
    for (const handle of handles) {
      expect(displayValuesFor(handle.element)).toEqual(['none'])
    }
    w.unmount()
  })

  it('leaves every handle unhidden once the image is node-selected', async () => {
    const w = await mountWithImage()
    selectImageNode(w)
    await flushPromises()

    expect(w.get('[data-resize-container]').classes()).toContain('ProseMirror-selectednode')
    const handles = w.findAll('[data-resize-handle]')
    expect(handles.length).toBe(8)
    for (const handle of handles) {
      expect(displayValuesFor(handle.element)).toEqual([])
    }
    w.unmount()
  })

  // Read-only is checked against a constructed DOM rather than a disabled mount, because a disabled
  // field has no handle elements left to select -- upstream removes them when the editor stops
  // being editable, and "renders no resize handles when mounted already disabled" above pins that.
  // What this adds is the belt-and-braces stylesheet rule: were a handle present anyway, a
  // read-only field must hide it even while the node carries the selection class.
  it('hides a handle in a read-only field even when the image is node-selected', () => {
    const root = document.createElement('div')
    root.className = 'rich-text__content'
    root.innerHTML =
      '<div class="ProseMirror" contenteditable="false">' +
      '<div data-resize-container class="ProseMirror-selectednode">' +
      '<div data-resize-wrapper><img><div data-resize-handle="top-left"></div></div>' +
      '</div></div>'
    const handle = root.querySelector('[data-resize-handle]')!
    expect(displayValuesFor(handle)).toContain('none')
  })
})

describe('RichTextInput resize handle positioning contract', () => {
  it.each(['top', 'bottom'])('centers the %s edge handle on its horizontal axis', (direction) => {
    expect(declarationsFor(direction)).toContain('margin-inline: auto')
  })

  it.each(['left', 'right'])('centers the %s edge handle on its vertical axis', (direction) => {
    expect(declarationsFor(direction)).toContain('margin-block: auto')
  })

  // The corners must stay pinned to their corners: an auto margin on either axis would centre them
  // too, and the eight positions would collapse again from the other direction.
  it.each(['top-left', 'top-right', 'bottom-left', 'bottom-right'])(
    'leaves the %s corner handle uncentered',
    (direction) => {
      expect(declarationsFor(direction)).not.toContain('auto')
    },
  )

  // Each handle straddles its edge by half its own size. The offset has to be a negative length
  // derived from the size, and it has to sit on the side the handle is pinned to.
  const OFFSET = 'var(--resize-handle-offset)'
  it.each([
    ['top-left', ['margin-top', 'margin-left']],
    ['top-right', ['margin-top', 'margin-right']],
    ['bottom-left', ['margin-bottom', 'margin-left']],
    ['bottom-right', ['margin-bottom', 'margin-right']],
    ['top', ['margin-top']],
    ['bottom', ['margin-bottom']],
    ['left', ['margin-left']],
    ['right', ['margin-right']],
  ] as const)('offsets the %s handle outward on %s', (direction, properties) => {
    for (const property of properties) {
      expect(declarationsFor(direction)).toContain(`${property}: ${OFFSET}`)
    }
  })

  it('derives the straddle offset from the handle size so the two cannot drift apart', () => {
    const base = styleRules.find((r) => r.selectors.includes('.rich-text__content [data-resize-handle]'))
    expect(base?.body).toContain('--resize-handle-offset: calc(var(--resize-handle-size) / -2)')
    expect(base?.body).toContain('width: var(--resize-handle-size)')
    expect(base?.body).toContain('height: var(--resize-handle-size)')
  })

  // The one edit that looks safe and is not: an outward offset written on an edge handle's LONG
  // axis replaces the auto margin that resolves upstream's over-constrained inline insets, and the
  // handle collapses back onto a corner.
  it.each([
    ['top', ['margin-left', 'margin-right']],
    ['bottom', ['margin-left', 'margin-right']],
    ['left', ['margin-top', 'margin-bottom']],
    ['right', ['margin-top', 'margin-bottom']],
  ] as const)('writes no length on the %s handle\'s auto-margin axis', (direction, properties) => {
    for (const property of properties) {
      expect(declarationsFor(direction)).not.toContain(property)
    }
  })
})
