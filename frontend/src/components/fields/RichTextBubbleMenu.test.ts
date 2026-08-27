import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises, DOMWrapper, type VueWrapper } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { createI18n } from 'vue-i18n'
import { useEditor, EditorContent, type Editor } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'
import Subscript from '@tiptap/extension-subscript'
import Superscript from '@tiptap/extension-superscript'
import RichTextBubbleMenu from './RichTextBubbleMenu.vue'
import { RICH_TEXT_COMMANDS, type RichTextCommand } from './richTextCommands'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { richtext: {
    bold: 'Bold', italic: 'Italic', strikethrough: 'Strikethrough',
    subscript: 'Subscript', superscript: 'Superscript', link: 'Link',
    selectionToolbar: 'Text formatting',
  } } } },
})
const globalOpts = { plugins: [i18n] }

// jsdom implements neither Range method: ProseMirror's own focus command chains a scrollIntoView
// that measures the caret via Range.getClientRects()/getBoundingClientRect(), and without these
// stubs that measurement throws asynchronously the moment a test focuses a real, attached editor
// -- RichTextInput.test.ts applies the same fix for the same reason.
if (!Range.prototype.getClientRects) {
  Range.prototype.getClientRects = () => [] as unknown as DOMRectList
}
if (!Range.prototype.getBoundingClientRect) {
  Range.prototype.getBoundingClientRect = () => ({
    top: 0, bottom: 0, left: 0, right: 0, width: 0, height: 0, x: 0, y: 0, toJSON: () => undefined,
  }) as DOMRect
}

// A small harness rather than mounting the whole RichTextInput: the bubble menu's predicate and
// its BubbleMenuPlugin wiring need a real editor with a real, focusable DOM node, but nothing else
// RichTextInput carries (toolbar, table menus, image dialog) is this component's concern.
const Harness = defineComponent({
  props: { content: { type: String, default: '<p>Hello world</p>' }, disabled: Boolean },
  emits: ['run'],
  setup(props, { emit, expose }) {
    const editor = useEditor({
      content: props.content,
      editable: !props.disabled,
      extensions: [
        StarterKit.configure({ link: false }),
        Link.configure({ openOnClick: false }),
        Image.configure({ inline: false }),
        Subscript.extend({ excludes: 'superscript' }),
        Superscript.extend({ excludes: 'subscript' }),
      ],
    })
    expose({ editor })
    return () => (editor.value
      ? h('div', [
          h(EditorContent, { editor: editor.value }),
          h(RichTextBubbleMenu, {
            editor: editor.value,
            disabled: props.disabled,
            onRun: (cmd: unknown) => emit('run', cmd),
          }),
        ])
      : null)
  },
})

// Every host this file attaches to, so afterEach can take them back out. teardown() below still
// removes the container on the happy path, but a body-level teardown is skipped the moment an
// assertion above it throws -- and this file's assertions read document.body, so one leaked
// container would put a second, stale `.rich-text__bubble` in front of every later test's query.
const attachedContainers: HTMLElement[] = []

function mountHarness(content = '<p>Hello world</p>', disabled = false): { w: VueWrapper; container: HTMLElement } {
  const container = document.body.appendChild(document.createElement('div'))
  attachedContainers.push(container)
  const w = mount(Harness, { props: { content, disabled }, global: globalOpts, attachTo: container })
  return { w, container }
}

function getEditor(w: VueWrapper): Editor {
  return (w.vm as unknown as { editor: Editor }).editor
}

// Walks the real doc to find `needle`'s own text range, rather than hardcoding positions -- keeps
// the helper honest against whatever fixture markup a test passes in.
function selectWord(editor: Editor, needle: string): void {
  let from = -1
  let to = -1
  editor.state.doc.descendants((node, pos) => {
    if (from !== -1 || !node.isText || !node.text) return
    const i = node.text.indexOf(needle)
    if (i === -1) return
    from = pos + i
    to = from + needle.length
  })
  if (from === -1) throw new Error(`"${needle}" not found in document`)
  editor.commands.setTextSelection({ from, to })
}

// Same walk as selectWord, but spanning from the start of `fromNeedle` to the end of `toNeedle` --
// for building a selection that crosses a block boundary, which a single-needle selectWord cannot.
function selectRange(editor: Editor, fromNeedle: string, toNeedle: string): void {
  let from = -1
  let to = -1
  editor.state.doc.descendants((node, pos) => {
    if (!node.isText || !node.text) return
    if (from === -1) {
      const i = node.text.indexOf(fromNeedle)
      if (i !== -1) from = pos + i
    }
    const j = node.text.indexOf(toNeedle)
    if (j !== -1) to = pos + j + toNeedle.length
  })
  if (from === -1 || to === -1) throw new Error(`range "${fromNeedle}".."${toNeedle}" not found in document`)
  editor.commands.setTextSelection({ from, to })
}

function teardown(w: VueWrapper, container: HTMLElement): void {
  w.unmount()
  container.remove()
}

// The plugin's own update() routes a selection/doc change through `window.setTimeout` in
// handleDebouncedUpdate, at updateDelay's default of 250ms (left unset here, so upstream's default
// applies) -- that timer, not any Vue reactivity, is what gates whether show()/hide() has run by
// the time an assertion reads the DOM. Reaching it costs more than those 250ms alone: tiptap's
// focus command defers view.focus() into a requestAnimationFrame (@tiptap/core's focus.ts), and
// the plugin's own focusHandler then schedules a zero-delay setTimeout before the 250ms one, so
// roughly 266ms of chained deferrals stand between an editor.commands.focus() and a settled DOM.
//
// Advanced on vitest's fake clock rather than waited out on the real one. A real-clock wait long
// enough to cover that chain is still only a margin, and a margin is a race: with this suite run
// concurrently against itself, so the timers land late, the previous real 300ms wait failed here
// repeatedly. Virtual time has no margin to lose -- the timers cannot fire after the assertion.
// advanceTimersByTimeAsync, not the synchronous form, because each hop needs the microtask queue
// drained for ProseMirror's and Vue's own promise work in between. @vue/test-utils' flushPromises
// keeps working under the fake clock: it captures the real setImmediate at module load.
const BUBBLE_MENU_SETTLE_MS = 300

async function settle(): Promise<void> {
  await vi.advanceTimersByTimeAsync(BUBBLE_MENU_SETTLE_MS)
  await flushPromises()
}

// Queries document.body, not the wrapper: RichTextBubbleMenu appends its BubbleMenuPlugin element
// into a private container that is itself a child of document.body (see the component's own script
// setup), so once shown the menu's root is a descendant of body, not of anything mount() attached --
// a wrapper.find() would never see it. find() searches descendants, so the extra container level
// between body and the menu root does not matter here.
function bubbleRoot() {
  return new DOMWrapper(document.body).find('.rich-text__bubble')
}

const INLINE_IDS = RICH_TEXT_COMMANDS.filter((c) => c.group === 'inline').map((c) => c.id)

describe('RichTextBubbleMenu', () => {
  // File-wide, because every test here is gated on the plugin's debounce (see settle above), and
  // installed before mount so the fake clock owns the whole chain of deferrals the editor sets up.
  // Restored in afterEach rather than left to the suite's restoreMocks/clearMocks settings, which
  // reset spies but not the clock.
  beforeEach(() => { vi.useFakeTimers() })
  afterEach(() => {
    vi.useRealTimers()
    attachedContainers.splice(0).forEach((el) => { el.remove() })
  })

  it('is absent from the DOM before anything is selected', async () => {
    const { w, container } = mountHarness()
    await flushPromises()
    expect(bubbleRoot().exists()).toBe(false)
    teardown(w, container)
  })

  it('appears once a non-empty text selection exists in a focused editor, carrying the accessible name', async () => {
    const { w, container } = mountHarness('<p>Hello world</p>')
    await flushPromises()
    const editor = getEditor(w)
    selectWord(editor, 'world')
    editor.commands.focus()
    await settle()
    const root = bubbleRoot()
    expect(root.exists()).toBe(true)
    expect(root.attributes('role')).toBe('toolbar')
    expect(root.attributes('aria-label')).toBe('Text formatting')
    teardown(w, container)
  })

  // Asserts the sequence within the menu root, not merely that each id exists -- and derives the
  // expected sequence from RICH_TEXT_COMMANDS itself (INLINE_IDS above) rather than a hand-typed
  // list, so the component's own filter cannot drift from the registry's current `inline` group
  // without this failing (confirmed: dropping the component's group filter fails this test). It
  // does NOT guard the registry's grouping itself -- if a command's `group` changed, both this
  // expectation and the component's filter would move together. That guard is
  // richTextCommands.test.ts's job: it pins the `inline` group's exact membership and order
  // directly against the registry, independent of this component.
  it('renders exactly the six inline commands, in registry order', async () => {
    const { w, container } = mountHarness('<p>Hello world</p>')
    await flushPromises()
    const editor = getEditor(w)
    selectWord(editor, 'world')
    editor.commands.focus()
    await settle()
    const root = bubbleRoot()
    const ids = root.findAll('[data-cmd]').map((el) => el.attributes('data-cmd'))
    expect(ids).toEqual(INLINE_IDS)
    teardown(w, container)
  })

  // That the bubble menu carries inline text formatting and nothing else is the point of this test, and like the order test
  // above it guards component-vs-registry drift, not the registry's own grouping: every data-cmd
  // rendered here must resolve back to whatever RICH_TEXT_COMMANDS currently reports as 'inline'
  // (confirmed: dropping the component's group filter fails this test too, on 'alignLeft'). If the
  // registry's own grouping were ever wrong, richTextCommands.test.ts is what would catch it --
  // this test would move in lockstep with a bad regrouping, not against it.
  it('renders no block, insert or history command', async () => {
    const { w, container } = mountHarness('<p>Hello world</p>')
    await flushPromises()
    const editor = getEditor(w)
    selectWord(editor, 'world')
    editor.commands.focus()
    await settle()
    const root = bubbleRoot()
    const ids = root.findAll('[data-cmd]').map((el) => el.attributes('data-cmd'))
    expect(ids.length).toBeGreaterThan(0)
    for (const id of ids) {
      const cmd = RICH_TEXT_COMMANDS.find((c) => c.id === id)
      expect(cmd?.group, id).toBe('inline')
    }
    teardown(w, container)
  })

  // The emitted payload must be the exact command object the registry holds, not its id or index
  // -- RichTextInput.vue's runCommand calls `command.run(editor.value, commandContext)` on
  // whatever this emits, so a wrong object (e.g. a different command) would run the wrong command,
  // and a bare id string would throw a TypeError at that call site (`command.run` is undefined),
  // not silently do nothing.
  it('clicking a command emits run with that exact command object', async () => {
    const { w, container } = mountHarness('<p>Hello world</p>')
    await flushPromises()
    const editor = getEditor(w)
    selectWord(editor, 'world')
    editor.commands.focus()
    await settle()
    await bubbleRoot().get('[data-cmd="italic"]').trigger('click')
    const emitted = w.emitted('run') as RichTextCommand[][] | undefined
    expect(emitted).toHaveLength(1)
    expect(emitted![0][0]).toBe(RICH_TEXT_COMMANDS.find((c) => c.id === 'italic'))
    teardown(w, container)
  })

  // A disabled ProseMirror view can never actually take real DOM focus (verified directly in
  // RichTextInput.test.ts: EditorView.focus() no-ops when !editable), so real focus cannot be used
  // to isolate this rule the way test 2 does. Forcing hasFocus() to answer true isolates
  // editor.isEditable as the one signal standing between this selection and the menu, rather than
  // re-proving that a disabled view can't be focused (already pinned elsewhere).
  it('never shows for a disabled (read-only) editor, even with a selection', async () => {
    const { w, container } = mountHarness('<p>Hello world</p>', true)
    await flushPromises()
    const editor = getEditor(w)
    selectWord(editor, 'world')
    editor.view.hasFocus = () => true
    await settle()
    expect(bubbleRoot().exists()).toBe(false)
    teardown(w, container)
  })

  // Task 1's predicate excludes a selected image by testing "the selected range carries no text",
  // but its own tests fabricate textBetween -- nothing before this proved that a real selected
  // image actually presents that way. This settles it against a real editor and a real image node:
  // first the raw claim (textBetween is actually empty over a NodeSelection's own range), then the
  // composed behaviour (the mounted menu stays out of the DOM).
  it('never shows for a selected image, and a real image NodeSelection really does carry no text', async () => {
    const { w, container } = mountHarness('<p>before</p><img src="x.png" alt="a" /><p>after</p>')
    await flushPromises()
    const editor = getEditor(w)
    let imagePos = -1
    editor.state.doc.descendants((node, pos) => {
      if (node.type.name === 'image') imagePos = pos
    })
    expect(imagePos).toBeGreaterThan(-1)
    editor.commands.setNodeSelection(imagePos)
    expect(editor.state.selection.empty).toBe(false)
    const { from, to } = editor.state.selection
    expect(editor.state.doc.textBetween(from, to)).toBe('')
    editor.commands.focus()
    await settle()
    expect(bubbleRoot().exists()).toBe(false)
    teardown(w, container)
  })

  // shouldShowBubbleMenu excludes a selection whose range touches only mark-disallowing textblocks
  // by walking state.doc.nodesBetween, but its own tests (richTextSelection.test.ts) fabricate that
  // shape -- nothing before this proved a real code block actually presents that way. This settles it
  // against a real editor and a real code block: first the raw claims (a real codeBlock node's spec
  // really does declare `marks: ''`, and toggling bold really is unavailable with the selection
  // inside one), then the composed behaviour (the mounted menu stays out of the DOM).
  it('never shows for a selection inside a code block, and a real code block really disallows all marks', async () => {
    const { w, container } = mountHarness('<pre><code>const x = 1</code></pre>')
    await flushPromises()
    const editor = getEditor(w)
    expect(editor.schema.nodes.codeBlock.spec.marks).toBe('')
    selectWord(editor, 'x')
    expect(editor.state.selection.$from.parent.type.spec.marks).toBe('')
    expect(editor.can().toggleBold()).toBe(false)
    editor.commands.focus()
    await settle()
    expect(bubbleRoot().exists()).toBe(false)
    teardown(w, container)
  })

  // The previous, $from.parent-keyed version of this rule was only narrowed, not closed: Mod-a's
  // real command is editor.commands.selectAll(), which produces an AllSelection whose $from resolves
  // at the doc itself (spec.marks undefined there), so it showed every button, inert, for a
  // select-all inside a code-block-only field. Settles the fixed rule against a real editor, a real
  // select-all and a real code block: the selection really is non-empty, and every command this menu
  // offers really is unavailable, yet the old rule would have shown the menu regardless.
  it('never shows for a real select-all whose entire document is a single code block', async () => {
    const { w, container } = mountHarness('<pre><code>const x = 1</code></pre>')
    await flushPromises()
    const editor = getEditor(w)
    editor.commands.selectAll()
    expect(editor.state.selection.empty).toBe(false)
    expect(editor.can().toggleBold()).toBe(false)
    editor.commands.focus()
    await settle()
    expect(bubbleRoot().exists()).toBe(false)
    teardown(w, container)
  })

  // The case the maintainer was willing to accept as over-hidden by the old rule: a selection
  // starting inside a code block and ending in a following paragraph. $from.parent-keyed logic hid
  // this (the selection "starts" in a mark-disallowing block), even though bold genuinely applies to
  // the paragraph tail -- confirmed here directly via editor.can().toggleBold(). Walking the whole
  // range instead of just where it starts is what makes this show correctly.
  it('shows for a real selection spanning a code block into a following paragraph, where bold really does apply', async () => {
    const { w, container } = mountHarness('<pre><code>const x = 1</code></pre><p>after</p>')
    await flushPromises()
    const editor = getEditor(w)
    selectRange(editor, 'x', 'after')
    expect(editor.state.selection.empty).toBe(false)
    expect(editor.can().toggleBold()).toBe(true)
    editor.commands.focus()
    await settle()
    expect(bubbleRoot().exists()).toBe(true)
    teardown(w, container)
  })

  // The zero-character boundary the textblock-keyed rule (this branch's own prior commit) missed: a
  // selection that includes all of a code block's own text but reaches only to the very start of the
  // following paragraph -- zero characters of the paragraph. Reachable in a real editor by
  // Shift+Down out of a code block, or by dragging one position past the block boundary. Settles the
  // inline-walk rule against a real editor: the raw claim first (this exact range's own text is the
  // code block's content and nothing from the paragraph, and every command this menu offers is
  // unavailable), then the composed behaviour (the menu, whose six buttons would otherwise all be
  // dead, stays out of the DOM).
  it('never shows for a selection that ends at the zero-character start of the following paragraph', async () => {
    const { w, container } = mountHarness('<pre><code>abc</code></pre><p>after</p>')
    await flushPromises()
    const editor = getEditor(w)
    editor.commands.setTextSelection({ from: 1, to: 6 })
    expect(editor.state.selection.empty).toBe(false)
    expect(editor.state.doc.textBetween(1, 6)).toBe('abc')
    expect(editor.can().toggleBold()).toBe(false)
    editor.commands.focus()
    await settle()
    expect(bubbleRoot().exists()).toBe(false)
    teardown(w, container)
  })

  // Regression test for the appendTo: () => document.body defect: BubbleMenuPlugin's blur guard is
  // `this.element.parentNode?.contains(event.relatedTarget)`, and document.body.contains(x) is true
  // for every element on the page, so that guard swallowed every blur and hide() was never reached
  // (confirmed by running this test against that code: the menu survived). A blur to an unrelated,
  // focusable element elsewhere in the document -- not to the menu itself, and not the null
  // relatedTarget a click on dead space produces -- is exactly the case that guard was supposed to
  // let through to hide().
  it('hides once the editor blurs to a focusable element elsewhere in the document', async () => {
    const { w, container } = mountHarness()
    await flushPromises()
    const editor = getEditor(w)
    selectWord(editor, 'world')
    editor.commands.focus()
    await settle()
    expect(bubbleRoot().exists()).toBe(true)

    const elsewhere = document.body.appendChild(document.createElement('input'))
    editor.view.dom.dispatchEvent(new FocusEvent('blur', { relatedTarget: elsewhere }))
    await settle()

    expect(bubbleRoot().exists()).toBe(false)
    elsewhere.remove()
    teardown(w, container)
  })
})
