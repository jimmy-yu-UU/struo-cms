import { describe, it, expect } from 'vitest'
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

function mountHarness(content = '<p>Hello world</p>', disabled = false): { w: VueWrapper; container: HTMLElement } {
  const container = document.body.appendChild(document.createElement('div'))
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

function teardown(w: VueWrapper, container: HTMLElement): void {
  w.unmount()
  container.remove()
}

// The plugin's own update() routes a selection/doc change through `window.setTimeout` in
// handleDebouncedUpdate, at updateDelay's default of 250ms (left unset here, so upstream's default
// applies) -- that timer, not any Vue reactivity, is what gates whether show()/hide() has run by
// the time an assertion reads the DOM. Real timers per the plan's Context (fake timers interact
// awkwardly with tiptap/vue-3's own rAF pair elsewhere in this file); 300ms clears the 250ms window
// with margin.
async function settle(): Promise<void> {
  await new Promise((resolve) => { setTimeout(resolve, 300) })
  await flushPromises()
}

// Queries document.body, not the wrapper: RichTextBubbleMenu configures BubbleMenuPlugin with
// appendTo: () => document.body (see the component's own template comment), so once shown the menu's
// root is a child of body, not of anything mount() attached -- a wrapper.find() would never see it.
function bubbleRoot() {
  return new DOMWrapper(document.body).find('.rich-text__bubble')
}

const INLINE_IDS = RICH_TEXT_COMMANDS.filter((c) => c.group === 'inline').map((c) => c.id)

describe('RichTextBubbleMenu', () => {
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

  // The new rule in shouldShowBubbleMenu excludes a selection whose parent block disallows marks
  // by testing $from.parent.type.spec.marks, but its own tests (richTextSelection.test.ts) fabricate
  // that shape -- nothing before this proved a real code block actually presents that way. This
  // settles it against a real editor and a real code block: first the raw claims (a real codeBlock
  // node's spec really does declare `marks: ''`, and toggling bold really is unavailable with the
  // selection inside one), then the composed behaviour (the mounted menu stays out of the DOM).
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
})
