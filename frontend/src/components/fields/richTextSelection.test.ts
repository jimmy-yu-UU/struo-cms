import { describe, it, expect } from 'vitest'
import { shouldShowBubbleMenu, type BubbleMenuShouldShowArgs } from './richTextSelection'

function args(over: Partial<BubbleMenuShouldShowArgs> = {}): BubbleMenuShouldShowArgs {
  return {
    editor: { isEditable: true },
    element: document.createElement('div'),
    view: { hasFocus: () => true },
    state: { selection: { empty: false }, doc: { textBetween: () => 'selected' } },
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
      state: { selection: { empty: true }, doc: { textBetween: () => 'selected' } },
    }))).toBe(false)
  })

  // This is the shape a text-free selection presents to this predicate: non-empty, no text.
  // Excluding it is what keeps this surface to inline text formatting; whether a real selected
  // image presents this shape is what Task 2 proves against a real editor, not this fabricated case.
  it('stays hidden for a non-empty selection that carries no text', () => {
    expect(shouldShowBubbleMenu(args({
      state: { selection: { empty: false }, doc: { textBetween: () => '' } },
    }))).toBe(false)
  })

  it('stays hidden when the selection is only whitespace', () => {
    expect(shouldShowBubbleMenu(args({
      state: { selection: { empty: false }, doc: { textBetween: () => '   \n ' } },
    }))).toBe(false)
  })

  it('asks the document for the text between the reported range, not some other range', () => {
    const seen: Array<[number, number]> = []
    shouldShowBubbleMenu(args({
      from: 4, to: 11,
      state: {
        selection: { empty: false },
        doc: { textBetween: (f: number, t: number) => { seen.push([f, t]); return 'x' } },
      },
    }))
    expect(seen).toEqual([[4, 11]])
  })
})
