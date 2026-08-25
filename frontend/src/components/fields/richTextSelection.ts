// The structural minimum TipTap's shouldShow argument object satisfies -- declared here rather than
// importing Editor/EditorView/EditorState so this module stays a plain function testable with
// fabricated arguments (see richTextSelection.test.ts). TipTap's real argument object satisfies this
// shape; Task 2 passes it straight through.
export interface BubbleMenuShouldShowArgs {
  editor: { isEditable: boolean }
  element: HTMLElement
  view: { hasFocus: () => boolean }
  state: {
    selection: { empty: boolean; $from: { parent: { type: { spec: { marks?: string } } } } }
    doc: { textBetween: (from: number, to: number) => string }
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

  // A code block declares `marks: ""` (no marks allowed at all), which is exactly how
  // codeBlock.spec.marks reads on a real editor -- confirmed directly against
  // editor.schema.nodes.codeBlock.spec.marks, and editor.can().toggleBold() (and every other
  // inline command this menu offers) returns false with the selection inside one. Written against
  // spec.marks generally, rather than checking the node's name, so any other mark-free node in a
  // fork's schema gets the same treatment without this rule needing to know its name.
  if (state.selection.$from.parent.type.spec.marks === '') return false

  return true
}
