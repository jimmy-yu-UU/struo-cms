import { describe, it, expect } from 'vitest'
import { shouldShowBubbleMenu, type BubbleMenuShouldShowArgs, type BubbleMenuShouldShowTextblock } from './richTextSelection'

// undefined, not '': this is the shape an ordinary block (paragraph, heading, ...) presents --
// spec.marks is undefined when a node declares no explicit restriction, meaning "all marks
// allowed". '' (no marks allowed at all) is a code block's own shape, exercised separately below.
// childCount: 1 on both -- each carries one text child, which is what makes them genuinely
// markable rather than merely eligible; the childCount: 0 case has its own constant below.
const ORDINARY_TEXTBLOCK: BubbleMenuShouldShowTextblock = { isTextblock: true, type: { spec: { marks: undefined } }, childCount: 1 }
const CODE_BLOCK_TEXTBLOCK: BubbleMenuShouldShowTextblock = { isTextblock: true, type: { spec: { marks: '' } }, childCount: 1 }

// An empty paragraph: isTextblock and spec.marks both read exactly like ORDINARY_TEXTBLOCK, but
// childCount 0 -- this is the shape TipTap's own TrailingNode extension leaves behind (confirmed
// directly against a real editor in RichTextBubbleMenu.test.ts), and the reason childCount is part
// of this rule at all rather than isTextblock/marks alone.
const EMPTY_MARKABLE_TEXTBLOCK: BubbleMenuShouldShowTextblock = { isTextblock: true, type: { spec: { marks: undefined } }, childCount: 0 }

// A block container that is not itself a textblock (e.g. a table or blockquote wrapper): its own
// spec typically leaves marks undefined too (only textblocks meaningfully restrict marks), and it
// has children, but a mark cannot attach to the container itself -- only to markable textblocks
// nested inside it, which get visited and judged separately by this same walk.
const NON_TEXTBLOCK_CONTAINER: BubbleMenuShouldShowTextblock = { isTextblock: false, type: { spec: { marks: undefined } }, childCount: 1 }

// Fabricates nodesBetween the way prosemirror-model's real Node#nodesBetween behaves for this
// module's purposes: calling the callback once per node the walk (would have) touched, in order.
function nodesBetweenOf(...nodes: BubbleMenuShouldShowTextblock[]): BubbleMenuShouldShowArgs['state']['doc']['nodesBetween'] {
  return (_from, _to, callback) => { nodes.forEach((node) => callback(node)) }
}

function args(over: Partial<BubbleMenuShouldShowArgs> = {}): BubbleMenuShouldShowArgs {
  return {
    editor: { isEditable: true },
    element: document.createElement('div'),
    view: { hasFocus: () => true },
    state: {
      selection: { empty: false },
      doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(ORDINARY_TEXTBLOCK) },
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
      state: { selection: { empty: true }, doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(ORDINARY_TEXTBLOCK) } },
    }))).toBe(false)
  })

  // This is the shape a text-free selection presents to this predicate: non-empty, no text.
  // Excluding it is what keeps this surface to inline text formatting; whether a real selected
  // image presents this shape is what Task 2 proves against a real editor, not this fabricated case.
  it('stays hidden for a non-empty selection that carries no text', () => {
    expect(shouldShowBubbleMenu(args({
      state: { selection: { empty: false }, doc: { textBetween: () => '', nodesBetween: nodesBetweenOf(ORDINARY_TEXTBLOCK) } },
    }))).toBe(false)
  })

  it('stays hidden when the selection is only whitespace', () => {
    expect(shouldShowBubbleMenu(args({
      state: { selection: { empty: false }, doc: { textBetween: () => '   \n ', nodesBetween: nodesBetweenOf(ORDINARY_TEXTBLOCK) } },
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
          nodesBetween: nodesBetweenOf(ORDINARY_TEXTBLOCK),
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
          nodesBetween: (f: number, t: number, callback) => { seen.push([f, t]); callback(ORDINARY_TEXTBLOCK) },
        },
      },
    }))
    expect(seen).toEqual([[4, 11]])
  })

  // A code block declares `marks: ""` (no marks at all), unlike an ordinary block's `undefined`
  // (all marks allowed) -- this is the fabricated shape for that rule; whether a real code block
  // actually presents this way is what the RichTextBubbleMenu.test.ts real-editor test proves,
  // not this fabricated case (the same gap the image case above already notes).
  it('stays hidden when the selected range touches only textblocks that disallow marks (e.g. entirely inside a code block)', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(CODE_BLOCK_TEXTBLOCK) },
      },
    }))).toBe(false)
  })

  it('still shows when the selected range touches a textblock with no mark restriction at all', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(ORDINARY_TEXTBLOCK) },
      },
    }))).toBe(true)
  })

  // The regression this rule replaces the $from.parent-keyed version to fix: Mod-a's AllSelection
  // resolves $from at depth 0, so a rule asking "what block does $from start in" saw the doc node
  // itself (spec.marks undefined) and showed the menu no matter what the doc actually contained.
  // Walking the whole range instead means what the FIRST node happens to be is irrelevant -- only
  // whether ANY node in the range allows marks decides it. This fabricates a range that starts in a
  // mark-disallowing block and ends in a markable one (a code block followed by a paragraph); the
  // old rule hid it (wrongly, since bold still applies to the paragraph tail), this one shows it.
  it('shows when the selected range spans a mark-disallowing block and a markable one, regardless of which one it starts in', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(CODE_BLOCK_TEXTBLOCK, ORDINARY_TEXTBLOCK) },
      },
    }))).toBe(true)
  })

  it('stays hidden when the selected range touches only mark-disallowing textblocks, even several of them', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(CODE_BLOCK_TEXTBLOCK, CODE_BLOCK_TEXTBLOCK) },
      },
    }))).toBe(false)
  })

  // The gap plain isTextblock/marks walking leaves: an empty textblock allows marks in principle but
  // has nothing for a mark to attach to (confirmed against a real editor: editor.can().toggleBold()
  // is false over exactly this shape, in RichTextBubbleMenu.test.ts's select-all test). Without the
  // childCount guard, this fabricated case would show the menu; with it, it stays hidden.
  it('stays hidden when the only markable textblock in range is empty (e.g. select-all leaves a code block plus an empty trailing paragraph)', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(CODE_BLOCK_TEXTBLOCK, EMPTY_MARKABLE_TEXTBLOCK) },
      },
    }))).toBe(false)
  })

  it('shows when the range touches an empty markable textblock alongside a genuinely markable one', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(EMPTY_MARKABLE_TEXTBLOCK, ORDINARY_TEXTBLOCK) },
      },
    }))).toBe(true)
  })

  // isTextblock is checked, not just marks/childCount, because a container node (table, blockquote)
  // typically leaves spec.marks undefined too and has children -- but a mark cannot attach to the
  // container itself, only to a markable textblock nested inside it (visited separately by this same
  // walk). Without the isTextblock check, this fabricated case would show the menu on the container
  // alone; with it, a range touching nothing but such a container stays hidden.
  it('stays hidden when the only permissive node in range is a container, not itself a textblock (e.g. a table or blockquote wrapper)', () => {
    expect(shouldShowBubbleMenu(args({
      state: {
        selection: { empty: false },
        doc: { textBetween: () => 'selected', nodesBetween: nodesBetweenOf(NON_TEXTBLOCK_CONTAINER) },
      },
    }))).toBe(false)
  })
})
