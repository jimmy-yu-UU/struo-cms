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

  // The toolbar renders the three segments with its dropdown components spliced between them, so a
  // command that belongs to no segment would silently disappear from the UI while still being
  // offered to RT-5/RT-7 from the flat list.
  it('concatenates the three toolbar segments into RICH_TEXT_COMMANDS, in order', () => {
    expect(RICH_TEXT_COMMANDS).toEqual([
      ...TOOLBAR_BEFORE_HEADINGS, ...TOOLBAR_BEFORE_COLOR, ...TOOLBAR_AFTER_TABLE,
    ])
  })

  // Four of these keys are not derivable from the id (strike/orderedList/hr/image), so a typo is a
  // realistic failure and would surface only as a missing tooltip and a missing accessible name.
  it('points every labelKey at a string that exists in the shipped en pack', () => {
    for (const c of RICH_TEXT_COMMANDS) {
      expect(typeof resolve(c.labelKey), c.id).toBe('string')
    }
  })

  // The groups are the contract RT-5 (inline) and RT-7 (block + insert) consume. Pinning the exact
  // membership here is what makes a later batch's surface change visible in THIS file's diff.
  it('partitions the commands into the groups the later batches consume', () => {
    expect(idsOf('inline')).toEqual(['bold', 'italic', 'strike', 'subscript', 'superscript', 'link'])
    expect(idsOf('block')).toEqual([
      'alignLeft', 'alignCenter', 'alignRight', 'alignJustify',
      'bulletList', 'orderedList', 'blockquote', 'codeBlock',
    ])
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

  // RichTextInput.test.ts holds an independent copy of this string and asserts what cn() does with
  // it; the two must not drift.
  it('keeps the active-state override class byte-identical to the toolbar contract', () => {
    expect(RICHTEXT_ACTIVE_BUTTON_CLASS).toBe(
      'data-[active=true]:bg-primary data-[active=true]:text-primary-foreground '
      + 'data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground '
      + 'dark:data-[active=true]:hover:bg-primary',
    )
  })
})
