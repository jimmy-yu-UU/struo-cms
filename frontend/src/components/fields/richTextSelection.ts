// The structural minimum TipTap's shouldShow argument object satisfies -- declared here rather than
// importing Editor/EditorView/EditorState so this module stays a plain function testable with
// fabricated arguments (see richTextSelection.test.ts). TipTap's real argument object satisfies this
// shape; Task 2 passes it straight through.
// A textblock node as this module needs to see one: enough of ProseMirror's real Node shape to ask
// "does this node hold inline content, does it allow marks at all, and does it actually contain
// anything a mark could attach to" -- nothing more. childCount is here for that last question: an
// empty textblock (childCount 0) allows marks in principle but has no inline content to carry one,
// which matters because TipTap's TrailingNode extension (bundled into StarterKit) inserts exactly
// such a node -- confirmed directly: selecting-all in a field whose only real content is a code block
// leaves the doc with an appended empty trailing paragraph, and that paragraph is what a rule keyed
// on isTextblock/marks alone would (wrongly) treat as the reason to show this menu.
export interface BubbleMenuShouldShowTextblock {
  isTextblock: boolean
  type: { spec: { marks?: string } }
  childCount: number
}

export interface BubbleMenuShouldShowArgs {
  editor: { isEditable: boolean }
  element: HTMLElement
  view: { hasFocus: () => boolean }
  state: {
    selection: { empty: boolean }
    doc: {
      textBetween: (from: number, to: number) => string
      // Matches prosemirror-model's Node#nodesBetween: walks every node overlapping [from, to] in
      // document order, depth-first. Only the node itself is declared here (not pos/parent/index),
      // since this module's callback ignores them.
      nodesBetween: (from: number, to: number, callback: (node: BubbleMenuShouldShowTextblock) => void | boolean) => void
    }
  }
  from: number
  to: number
}

export function shouldShowBubbleMenu(args: BubbleMenuShouldShowArgs): boolean {
  const { editor, element, view, state, from, to } = args

  if (!editor.isEditable) return false

  // A click on one of the menu's own buttons moves document.activeElement off the editor view --
  // so focus inside the menu's element has to count as focus too, or this predicate is asking the
  // wrong question about where the user's attention is. This is a browser-only failure mode with no
  // coverage in this test suite, and none is possible here: jsdom's trigger('click') moves no focus.
  const hasFocus = view.hasFocus() || element.contains(document.activeElement)
  if (!hasFocus) return false

  if (state.selection.empty) return false

  // "Has text" rather than "is a text selection" is the check used here so this module keeps
  // needing nothing but the shape above -- classifying a ProseMirror selection would mean importing
  // its selection classes, which this module deliberately does not do.
  if (!state.doc.textBetween(from, to).trim()) return false

  // Asks "can any command this menu offers apply somewhere in this selection", by walking every
  // textblock the selected RANGE touches, rather than "what block does the selection happen to start
  // in". The earlier, $from.parent-based version of this rule asked the second question, which
  // Mod-a's AllSelection answers wrong: an AllSelection's $from resolves at depth 0, so $from.parent
  // is the doc node itself, whose spec.marks is undefined ("no restriction") regardless of what the
  // doc contains -- so select-all inside a code-block-only field showed every button, inert. Walking
  // the range instead also makes a selection that starts in a code block and ends in a paragraph
  // correctly show, where the old rule hid it: bold still applies to the paragraph tail.
  //
  // A code block declares `marks: ""` (no marks allowed at all), which is exactly how
  // codeBlock.spec.marks reads on a real editor -- confirmed directly against
  // editor.schema.nodes.codeBlock.spec.marks, and editor.can().toggleBold() (and every other inline
  // command this menu offers) returns false with the selection entirely inside one. Written against
  // spec.marks generally, rather than checking the node's name, so any other mark-free node in a
  // fork's schema gets the same treatment without this rule needing to know its name.
  //
  // childCount > 0 is required too, not just isTextblock/marks: without it, select-all in a
  // code-block-only field still showed this menu, because TrailingNode's appended empty paragraph
  // satisfies isTextblock/marks on its own -- confirmed directly (RichTextBubbleMenu.test.ts) that
  // editor.can().toggleBold() is false there despite the paragraph "allowing" bold, since there is no
  // inline content in it for a mark to attach to. TipTap's own canSetMark command asks the equivalent
  // question at the inline-node level; childCount is the cheapest structural proxy for "there is
  // something here to check" without importing prosemirror-model into this module.
  let hasMarkableTextblock = false
  state.doc.nodesBetween(from, to, (node) => {
    if (node.isTextblock && node.type.spec.marks !== '' && node.childCount > 0) hasMarkableTextblock = true
  })
  if (!hasMarkableTextblock) return false

  return true
}
