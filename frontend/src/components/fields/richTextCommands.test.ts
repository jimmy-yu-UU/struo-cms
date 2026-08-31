import { describe, it, expect } from 'vitest'
import en from '../../locales/en'
import {
  RICH_TEXT_COMMANDS, RICHTEXT_ACTIVE_BUTTON_CLASS,
  TOOLBAR_BEFORE_HEADINGS, TOOLBAR_BEFORE_COLOR, TOOLBAR_AFTER_TABLE,
  type RichTextCommand, type RichTextCommandGroup,
} from './richTextCommands'

function resolve(key: string): unknown {
  return key.split('.').reduce<unknown>(
    (acc, part) => (acc && typeof acc === 'object' ? (acc as Record<string, unknown>)[part] : undefined),
    en as unknown,
  )
}

function idsOf(group: RichTextCommandGroup): string[] {
  return RICH_TEXT_COMMANDS.filter((c) => c.group === group).map((c) => c.id)
}

describe('richTextCommands', () => {
  it('gives every command a unique id', () => {
    const ids = RICH_TEXT_COMMANDS.map((c) => c.id)
    expect(new Set(ids).size).toBe(ids.length)
  })

  // The seams between these three arrays are where the heading, colour and table menus render, so
  // which segment a command sits in is part of the toolbar's visible order, and the flat list alone
  // cannot pin it: RICH_TEXT_COMMANDS is defined as the concatenation of these same three arrays, so
  // asserting the flat list equals that concatenation is a tautology that cannot fail, and the
  // frozen-order test below pins only the resulting flat id sequence, not which of the three arrays
  // each id came from -- moving a command across a segment boundary while keeping its neighbours in
  // the flat list unchanged (see the image/TOOLBAR_AFTER_TABLE example below) would leave both of
  // those checks green while visibly moving the button in the rendered toolbar.
  it('assigns each command to its correct toolbar segment', () => {
    expect(TOOLBAR_BEFORE_HEADINGS.map((c) => c.id)).toEqual([
      'bold', 'italic', 'strike', 'alignLeft', 'alignCenter', 'alignRight', 'alignJustify',
    ])
    expect(TOOLBAR_BEFORE_COLOR.map((c) => c.id)).toEqual([
      'subscript', 'superscript', 'bulletList', 'orderedList', 'blockquote', 'codeBlock',
      'link', 'hr', 'image',
    ])
    expect(TOOLBAR_AFTER_TABLE.map((c) => c.id)).toEqual(['undo', 'redo'])
  })

  // The group-partition test below pins order only WITHIN a group: moving the link literal above
  // bulletList inside TOOLBAR_BEFORE_COLOR leaves idsOf('inline') and idsOf('block') both unchanged.
  // Neither that test nor the per-segment test above constrains this flat sequence, so this test is
  // not the one place ordering is pinned end to end -- it is one half of that: the per-segment test
  // pins which of the three arrays a command lives in, this one pins the resulting flat id order,
  // and only the two together pin the toolbar's order completely.
  it('renders the eighteen commands in the exact frozen toolbar order', () => {
    expect(RICH_TEXT_COMMANDS.map((c) => c.id)).toEqual([
      'bold', 'italic', 'strike', 'alignLeft', 'alignCenter', 'alignRight', 'alignJustify',
      'subscript', 'superscript', 'bulletList', 'orderedList', 'blockquote', 'codeBlock',
      'link', 'hr', 'image', 'undo', 'redo',
    ])
  })

  // Four of these keys are not derivable from the id (strike/orderedList/hr/image), so a typo is a
  // realistic failure and would surface only as a missing tooltip and a missing accessible name.
  it('points every labelKey at a string that exists in the shipped en pack', () => {
    for (const c of RICH_TEXT_COMMANDS) {
      expect(typeof resolve(c.labelKey), c.id).toBe('string')
    }
  })

  // The loop above only proves each labelKey resolves to A string, not the RIGHT one -- strike
  // pointed at fields.richtext.bold would still pass it. These four are exactly the keys that don't
  // derive from the id, so they're the ones a copy-paste mistake would actually hit, and nothing
  // else in the suite asserts an aria-label's value to catch it another way.
  it('points the four non-derivable labelKeys at their correct entries', () => {
    const byId = new Map(RICH_TEXT_COMMANDS.map((c) => [c.id, c.labelKey]))
    expect(byId.get('strike')).toBe('fields.richtext.strikethrough')
    expect(byId.get('orderedList')).toBe('fields.richtext.numberedList')
    expect(byId.get('hr')).toBe('fields.richtext.horizontalRule')
    expect(byId.get('image')).toBe('fields.richtext.insertImage')
  })

  // Each group is a semantic slice of the registry, not a claim about who currently reads it --
  // 'inline' is what RichTextBubbleMenu.vue's floating toolbar filters on; the other groups are
  // available to whichever surface wants that slice. Headings and tables never entered this
  // registry, so no group covers them. Pinning the exact membership here is what makes any such
  // surface's contract change visible in THIS file's diff.
  it('partitions the commands into groups by the contract each one carries', () => {
    expect(idsOf('inline')).toEqual(['bold', 'italic', 'strike', 'subscript', 'superscript', 'link'])
    expect(idsOf('align')).toEqual(['alignLeft', 'alignCenter', 'alignRight', 'alignJustify'])
    expect(idsOf('block')).toEqual(['bulletList', 'orderedList', 'blockquote', 'codeBlock'])
    expect(idsOf('insert')).toEqual(['hr', 'image'])
    expect(idsOf('history')).toEqual(['undo', 'redo'])
  })

  // The button renders an icon component OR a glyph string, never both and never neither -- a
  // command with neither renders an empty, unlabelled-looking button.
  it('gives every command exactly one of icon and glyph', () => {
    for (const c of RICH_TEXT_COMMANDS) {
      expect(Boolean(c.icon) !== Boolean(c.glyph), c.id).toBe(true)
    }
    expect(RICH_TEXT_COMMANDS.filter((c) => c.glyphTag).map((c) => c.id))
      .toEqual(['bold', 'italic', 'strike'])
  })

  // These four commands have no active state in the editor, which is why the toolbar renders them
  // with no data-active attribute at all rather than data-active="false".
  it('leaves isActive null for exactly the four commands that cannot be active', () => {
    expect(RICH_TEXT_COMMANDS.filter((c: RichTextCommand) => c.isActive === null).map((c) => c.id))
      .toEqual(['hr', 'image', 'undo', 'redo'])
  })

  // RichTextInput.test.ts imports this constant and asserts what cn() does with it, keeping its own
  // toContain substrings hardcoded rather than derived from it; this test is the other half of that
  // pair, pinning the constant's own exact value.
  it('keeps the active-state override class byte-identical to the toolbar contract', () => {
    expect(RICHTEXT_ACTIVE_BUTTON_CLASS).toBe(
      'data-[active=true]:bg-primary data-[active=true]:text-primary-foreground '
      + 'data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground '
      + 'dark:data-[active=true]:hover:bg-primary',
    )
  })
})
