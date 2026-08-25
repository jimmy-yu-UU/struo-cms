import { describe, it, expect } from 'vitest'
import { shouldShowBubbleMenu, type BubbleMenuShouldShowArgs, type BubbleMenuShouldShowInlineNode, type BubbleMenuShouldShowParent } from './richTextSelection'

// isInline true is a text node's own shape; false is what every block-level node (paragraph,
// codeBlock, table, blockquote, ...) reports, whatever its own spec.marks says -- a mark cannot
// attach to a block node itself, only to inline content nested inside it.
const INLINE_NODE: BubbleMenuShouldShowInlineNode = { isInline: true }
const NON_INLINE_NODE: BubbleMenuShouldShowInlineNode = { isInline: false }

// undefined vs '' on the PARENT, not the node touched -- this rule checks whether the node
// containing a piece of inline content allows marks, mirroring TipTap's own canSetMark. undefined
// is an ordinary block's shape (all marks allowed); '' is a code block's, confirmed directly against
// a real editor (editor.schema.nodes.codeBlock.spec.marks, in RichTextBubbleMenu.test.ts).
const ORDINARY_PARENT: BubbleMenuShouldShowParent = { type: { spec: { marks: undefined } } }
const CODE_BLOCK_PARENT: BubbleMenuShouldShowParent = { type: { spec: { marks: '' } } }

// A markable piece of inline content sitting in an ordinary block, and one sitting in a code block
// -- the two entry shapes most of the cases below are built from.
const ORDINARY_ENTRY = { node: INLINE_NODE, parent: ORDINARY_PARENT }
const CODE_BLOCK_ENTRY = { node: INLINE_NODE, parent: CODE_BLOCK_PARENT }

// A block-level node touching the range with nothing inline inside it in that range -- what real
// nodesBetween calls back with for an empty paragraph, or for a paragraph touched only at its own
// zero-character boundary (see the boundary-case test below). Paired with an ORDINARY_PARENT here
// to prove the isInline gate itself: without it, this entry's own permissive parent would (wrongly)
// count as markable.
const NON_INLINE_ENTRY = { node: NON_INLINE_NODE, parent: ORDINARY_PARENT }

// Fabricates nodesBetween the way prosemirror-model's real Node#nodesBetween behaves for this
// module's purposes: calling the callback once per (node, parent) pair the walk (would have)
// touched, in order.
function nodesBetweenOf(
  ...entries: Array<{ node: BubbleMenuShouldShowInlineNode; parent: BubbleMenuShouldShowParent | null }>
): BubbleMenuShouldShowArgs['state']['doc']['nodesBetween'] {
  return (_from, _to, callback) => { entries.forEach(({ node, parent }) => callback(node, 0, parent)) }
}

function args(over: Partial<BubbleMenuShouldShowArgs> = {}): BubbleMenuShouldShowArgs {
  return {
    editor: { isEditable: true },
    element: document.createElement('div'),
    view: { hasFocus: () => true },
    state: {
      selection: { empty: false },
      doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(ORDINARY_ENTRY) },
    },
    from: 1,
    to: 9,
    ...over,
  }
}

describe('shouldShowBubbleMenu', () => {
  it('shows for a non-empty text selection in a focused, editable editor', () => {
    expect(shouldShowBubbleMenu(args())).toBe(true)
  })

  it('stays hidden on a read-only editor', () => {
    expect(shouldShowBubbleMenu(args({ editor: { isEditable: false } }))).toBe(false)
  })

  it('stays hidden when the editor is not focused', () => {
    expect(shouldShowBubbleMenu(args({ view: { hasFocus: () => false } }))).toBe(false)
  })

  // Without this, clicking a button inside the menu blurs the editor, the predicate goes false and
  // the menu disappears before the command runs -- so every one of its buttons would be dead.
  it('stays visible while focus is inside the menu itself', () => {
    const element = document.createElement('div')
    const inner = element.appendChild(document.createElement('button'))
    document.body.appendChild(element)
    inner.focus()
    expect(shouldShowBubbleMenu(args({ element, view: { hasFocus: () => false } }))).toBe(true)
    element.remove()
  })

  it('stays hidden for a collapsed caret', () => {
    expect(shouldShowBubbleMenu(args({
      state: { selection: { empty: true }, doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(ORDINARY_ENTRY) } },
    }))).toBe(false)
  })

  // This is the shape a text-free selection presents to this predicate: non-empty, no text.
  // Excluding it is what keeps this surface to inline text formatting; whether a real selected
  // image presents this shape is what Task 2 proves against a real editor, not this fabricated case.
  it('stays hidden for a non-empty selection that carries no text', () => {
    expect(shouldShowBubbleMenu(args({
      state: { selection: { empty: false }, doc: { textBetween: () => '', nodesBetween: nodesBetweenOf(ORDINARY_ENTRY) } },
    }))).toBe(false)
  })

  it('stays hidden when the selection is only whitespace', () => {
    expect(shouldShowBubbleMenu(args({
      state: { selection: { empty: false }, doc: { textBetween: () => '   \n ', nodesBetween: nodesBetweenOf(ORDINARY_ENTRY) } },
    }))).toBe(false)
  })

  it('asks the document for the text between the reported range, not some other range', () => {
    const seen: Array<[number, number]> = []
    shouldShowBubbleMenu(args({
      from: 4, to: 11,
      state: {
        selection: { empty: false },
        doc: {
          textBetween: (f: number, t: number) => { seen.push([f, t]); return 'x' },
          nodesBetween: nodesBetweenOf(ORDINARY_ENTRY),
        },
      },
    }))
    expect(seen).toEqual([[4, 11]])
  })

  it('walks nodes over the reported range, not some other range', () => {
    const seen: Array<[number, number]> = []
    shouldShowBubbleMenu(args({
      from: 4, to: 11,
      state: {
        selection: { empty: false },
        doc: {
          textBetween: () => 'x',
          nodesBetween: (f: number, t: number, callback) => { seen.push([f, t]); callback(INLINE_NODE, 0, ORDINARY_PARENT) },
        },
      },
    }))
    expect(seen).toEqual([[4, 11]])
  })

  // A code block's inline content has a parent declaring `marks: ""` (no marks at all), unlike an
  // ordinary block's `undefined` (all marks allowed) -- this is the fabricated shape for that rule;
  // whether a real code block actually presents this way is what the RichTextBubbleMenu.test.ts
  // real-editor test proves, not this fabricated case.
  it('stays hidden when the selected range touches only inline content whose parent disallows marks (e.g. entirely inside a code block)', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(CODE_BLOCK_ENTRY) },
      },
    }))).toBe(false)
  })

  it('still shows when the selected range touches inline content whose parent has no mark restriction at all', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(ORDINARY_ENTRY) },
      },
    }))).toBe(true)
  })

  // The regression this rule replaces the $from.parent-keyed version to fix: Mod-a's AllSelection
  // resolves $from at depth 0, so a rule asking "what block does $from start in" saw the doc node
  // itself (spec.marks undefined) and showed the menu no matter what the doc actually contained.
  // Walking the whole range instead means what the FIRST node happens to be is irrelevant -- only
  // whether ANY inline node in the range has a mark-allowing parent decides it. This fabricates a
  // range that touches inline content in a mark-disallowing block and inline content in a markable
  // one (a code block followed by a paragraph); the old rule hid it (wrongly, since bold still
  // applies to the paragraph's text), this one shows it.
  it('shows when the selected range spans mark-disallowing inline content and markable inline content, regardless of which comes first', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(CODE_BLOCK_ENTRY, ORDINARY_ENTRY) },
      },
    }))).toBe(true)
  })

  it('stays hidden when the selected range touches only mark-disallowing inline content, even several pieces of it', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(CODE_BLOCK_ENTRY, CODE_BLOCK_ENTRY) },
      },
    }))).toBe(false)
  })

  // Mutation-proof for the `node.isInline` check: without it, a block-level node's own permissive
  // parent would (wrongly) count as markable even though nothing inline is actually being touched.
  // This is exactly the shape a boundary selection produces -- see the combined case right below --
  // isolated here to a single entry so it proves this one clause on its own.
  it('stays hidden when the only node touching the range is not itself inline, even though its parent allows marks', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(NON_INLINE_ENTRY) },
      },
    }))).toBe(false)
  })

  // The zero-character boundary this walk fixes over the previous (textblock-keyed) version: a
  // selection that includes a code block's own inline content plus reaches only to the very start
  // of the following paragraph -- zero characters of the paragraph. Real nodesBetween calls back
  // for the paragraph itself (not itself inline) but never for any inline content inside it, since
  // none of that content overlaps the range. Confirmed against a real editor and a real selection
  // in RichTextBubbleMenu.test.ts.
  it('stays hidden at a selection boundary that touches a following block but none of its own inline content', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'abc', nodesBetween: nodesBetweenOf(CODE_BLOCK_ENTRY, NON_INLINE_ENTRY) },
      },
    }))).toBe(false)
  })

  it('shows when the range touches a non-inline node alongside genuinely markable inline content', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(NON_INLINE_ENTRY, ORDINARY_ENTRY) },
      },
    }))).toBe(true)
  })

  // Mutation-proof for the `parent` truthiness check: prosemirror-model's own Node#nodesBetween
  // types this parameter as nullable, even though state.doc.nodesBetween in practice always supplies
  // one (doc is always the parent handed to its own top-level children) -- this is the defensive
  // case for the type this rule declares, not a shape a real walk from state.doc is known to produce.
  it('stays hidden when an inline node is reported with no parent at all', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf({ node: INLINE_NODE, parent: null }) },
      },
    }))).toBe(false)
  })
})
