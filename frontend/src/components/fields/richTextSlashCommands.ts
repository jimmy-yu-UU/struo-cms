import type { Editor } from '@tiptap/vue-3'
import { RICH_TEXT_COMMANDS, type RichTextCommandContext } from './richTextCommands'
import { HEADING_LEVELS } from './richTextHeadings'

export interface RichTextSlashItem {
  id: string
  label: string
  aliases: ReadonlyArray<string>
  run: (editor: Editor, ctx: RichTextCommandContext) => void
}

// Aliases are identifiers, not copy -- translating them would break the one thing they exist for:
// reaching a command by typing ASCII right after "/", which is exactly where a zh-TW input method
// still is (it hasn't composed anything yet). They stay out of the locale files on purpose.
const SLASH_ALIASES: Readonly<Record<string, ReadonlyArray<string>>> = {
  bulletList: ['ul', 'bullet', 'list'],
  orderedList: ['ol', 'number', 'ordered'],
  blockquote: ['quote', 'blockquote'],
  codeBlock: ['code', 'pre'],
  // Not 'rule': it contains 'ul' as a substring, which would make filterSlashItems' plain
  // .includes() match this item for a query meant to reach only bulletList's 'ul' alias.
  hr: ['hr', 'divider', 'separator'],
  image: ['img', 'image', 'picture'],
}

// hr's toolbar tooltip key (horizontalRule -> "Horizontal rule") lowercases to a string that
// itself contains 'ul' (from "rule"), which would surface this item for a slash query meant to
// reach only bulletList's 'ul' alias via filterSlashItems' plain substring match. The slash menu
// uses its own, shorter label instead; the toolbar button keeps its own tooltip untouched.
const SLASH_LABEL_KEYS: Readonly<Record<string, string>> = {
  hr: 'fields.richtext.slashDivider',
}

function fromRegistry(
  group: 'block' | 'insert', t: (key: string) => string,
): RichTextSlashItem[] {
  return RICH_TEXT_COMMANDS
    .filter((c) => c.group === group)
    .map((c) => ({
      id: c.id,
      label: t(SLASH_LABEL_KEYS[c.id] ?? c.labelKey),
      aliases: SLASH_ALIASES[c.id] ?? [],
      run: c.run,
    }))
}

export function buildSlashItems(t: (key: string) => string): RichTextSlashItem[] {
  return [
    ...HEADING_LEVELS.map((level) => ({
      id: `heading${level}`,
      label: t(`fields.richtext.heading${level}`),
      aliases: [`h${level}`, `heading${level}`],
      run: (editor: Editor) => { editor.chain().focus().setHeading({ level }).run() },
    })),
    ...fromRegistry('block', t),
    // The toolbar's table entry is a grid popover plus a custom-size dialog, not a single command,
    // so it never joined RICH_TEXT_COMMANDS -- it has to be hand-authored here too.
    {
      id: 'table',
      label: t('fields.richtext.table'),
      aliases: ['table'],
      run: (editor: Editor) => {
        editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run()
      },
    },
    ...fromRegistry('insert', t),
  ]
}

export function filterSlashItems(
  items: ReadonlyArray<RichTextSlashItem>, query: string,
): RichTextSlashItem[] {
  const q = query.trim().toLowerCase()
  if (!q) return [...items]
  return items.filter(
    // No .toLowerCase() on the alias side: SLASH_ALIASES is pinned lowercase ASCII by
    // richTextSlashCommands.test.ts, so q (already lowercased above) compares correctly as-is.
    (item) => item.label.toLowerCase().includes(q) || item.aliases.some((a) => a.includes(q)),
  )
}
