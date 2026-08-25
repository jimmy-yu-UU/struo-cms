// The structural minimum TipTap's shouldShow argument object satisfies -- declared here rather than
// importing Editor/EditorView/EditorState so this module stays a plain function testable with
// fabricated arguments (see richTextSelection.test.ts). TipTap's real argument object satisfies this
// shape; Task 2 passes it straight through.
export interface BubbleMenuShouldShowArgs {
  editor: { isEditable: boolean }
  element: HTMLElement
  view: { hasFocus: () => boolean }
  state: { selection: { empty: boolean }; doc: { textBetween: (from: number, to: number) => string } }
  from: number
  to: number
}

export function shouldShowBubbleMenu(args: BubbleMenuShouldShowArgs): boolean {
  const { editor, element, view, state, from, to } = args

  if (!editor.isEditable) return false

  // A click on one of the menu's own buttons moves focus onto that button, which is not the editor
  // view -- without also treating focus inside the menu's element as "focused", the predicate would
  // go false on the same tick as the click and hide the menu before its command ran.
  const hasFocus = view.hasFocus() || element.contains(document.activeElement)
  if (!hasFocus) return false

  if (state.selection.empty) return false

  // "Has text" rather than "is a text selection" is the check used here so this module keeps
  // needing nothing but the shape above -- classifying a ProseMirror selection would mean importing
  // its selection classes, which this module deliberately does not do.
  if (!state.doc.textBetween(from, to).trim()) return false

  return true
}
