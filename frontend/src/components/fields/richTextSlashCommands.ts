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
  hr: ['hr', 'divider', 'rule'],
  image: ['img', 'image', 'picture'],
}

function fromRegistry(
  group: 'block' | 'insert', t: (key: string) => string,
): RichTextSlashItem[] {
  return RICH_TEXT_COMMANDS
    .filter((c) => c.group === group)
    .map((c) => ({
      id: c.id,
      label: t(c.labelKey),
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
    // Substring matching (not prefix, not exact) is intended here, the same as the label branch
    // below -- 'ul' matching both bulletList's own alias and hr's 'rule' alias is accepted
    // over-matching for a candidate list navigated with arrow keys, not a bug to eliminate.
    (item) => item.label.toLowerCase().includes(q) || item.aliases.some((a) => a.includes(q)),
  )
}
