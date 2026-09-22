// The structural minimum TipTap's shouldShow argument object satisfies -- declared here rather than
// importing Editor/EditorView/EditorState so this module stays a plain function testable with
// fabricated arguments (see richTextSelection.test.ts). TipTap's real argument object satisfies this
// shape; the caller passes it straight through.

// The node shape this module needs from ProseMirror's Node#nodesBetween walk: only whether the node
// is inline. isInline is false for every block-level node (paragraph, codeBlock, table, blockquote,
// ...) regardless of that node's own spec.marks, since a mark can never attach to a block node
// itself -- only to inline content nested inside it, which the same walk visits separately with its
// own parent.
export interface BubbleMenuShouldShowInlineNode {
  isInline: boolean
}

// The parent shape this module needs: enough of the containing node to ask "does this node allow
// marks at all". undefined means no restriction; a code block declares '' (no marks allowed at
// all) -- confirmed directly against a real editor's editor.schema.nodes.codeBlock.spec.marks (see
// RichTextBubbleMenu.test.ts).
export interface BubbleMenuShouldShowParent {
  type: { spec: { marks?: string } }
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
      // document order, depth-first, passing each node's own immediate parent (null only at the
      // walk's own root, which state.doc.nodesBetween never is -- doc itself is always the parent
      // supplied to its own top-level children). pos and index are not declared, since this
      // module's callback ignores them.
      nodesBetween: (
        from: number,
        to: number,
        callback: (node: BubbleMenuShouldShowInlineNode, pos: number, parent: BubbleMenuShouldShowParent | null) => void | boolean
      ) => void
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

  // Mirrors the check TipTap's own canSetMark command performs (@tiptap/core's setMark.ts): walk
  // every node the range touches, and count only a node that is itself inline, checked against ITS
  // OWN PARENT's spec.marks -- not the node's own. A block node (paragraph, codeBlock, ...) is never
  // itself inline, so it never counts on its own, whatever its spec.marks says; only inline content
  // nested inside it does. This is why no separate "does this block actually contain anything"
  // guard is needed: a block with no inline content in the walked range (an empty paragraph, or a
  // paragraph touched only at its own zero-character boundary) simply contributes no inline node to
  // check, structurally, rather than needing to be filtered out after the fact.
  //
  // Walking the whole range this way, rather than asking what block the selection happens to start
  // in, is what Mod-a's AllSelection needs: an AllSelection's $from resolves at depth 0, so
  // $from.parent is the doc node itself, whose spec.marks is undefined ("no restriction") regardless
  // of what the doc contains. Walking the range instead makes what the first node happens to be
  // irrelevant: only whether ANY inline node in the range has a mark-allowing parent decides it. It
  // also correctly shows a selection that starts in a code block and ends partway into a following
  // paragraph's own text, since bold still applies to the paragraph's text.
  //
  // A code block declares `marks: ""` (no marks allowed at all) on itself -- confirmed directly
  // against a real editor (editor.schema.nodes.codeBlock.spec.marks), and
  // editor.can().toggleBold() (and every other inline command this menu offers) returns false with
  // the selection entirely inside one. Checked against the PARENT's spec.marks generally, rather
  // than checking the parent's name, so any other mark-free node in a fork's schema gets the same
  // treatment without this rule needing to know its name.
  let hasMarkableInline = false
  state.doc.nodesBetween(from, to, (node, _pos, parent) => {
    if (node.isInline && parent && parent.type.spec.marks !== '') hasMarkableInline = true
  })
  if (!hasMarkableInline) return false

  return true
}
