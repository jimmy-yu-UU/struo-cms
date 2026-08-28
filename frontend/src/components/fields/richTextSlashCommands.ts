import type { Editor } from '@tiptap/vue-3'
import en from '../../locales/en'
import { RICH_TEXT_COMMANDS, type RichTextCommandContext } from './richTextCommands'
import { HEADING_LEVELS } from './richTextHeadings'

export interface RichTextSlashItem {
  id: string
  label: string
  // The English label for this same labelKey, lowercased, resolved against the English catalogue
  // regardless of which locale built this item. A translated label carries no ASCII under most
  // locales and SLASH_ALIASES only covers hand-picked short forms, so without this field a query
  // like "horizontal" or "numbered" would only ever reach the command it names when the UI itself
  // is in English.
  enLabel: string
  aliases: ReadonlyArray<string>
  run: (editor: Editor, ctx: RichTextCommandContext) => void
}

// Aliases are identifiers, not copy -- translating them would break the one thing they exist for:
// reaching a command by typing ASCII right after "/", which is exactly where a zh-TW input method
// still is (it hasn't composed anything yet). They stay out of the locale files on purpose.
//
// They are additive, not the only path to that goal: filterSlashItems also matches an item's
// English label (enLabel above), so a command is already reachable by its own name under any
// locale with zero entries here. This table exists only to shorten that further with short forms
// no English word supplies on its own (ul, ol, img, ...).
const SLASH_ALIASES: Readonly<Record<string, ReadonlyArray<string>>> = {
  bulletList: ['ul', 'bullet', 'list'],
  orderedList: ['ol', 'number', 'ordered'],
  blockquote: ['quote', 'blockquote'],
  codeBlock: ['code', 'pre'],
  hr: ['hr', 'divider', 'rule'],
  image: ['img', 'image', 'picture'],
}

// No shared "resolve a dotted key against a locale object" helper exists in this codebase outside
// a live i18n instance -- richTextSlashCommands.test.ts keeps its own local copy for the same
// reason. Kept private and this small rather than promoted to lib/: nothing else needs it.
function resolveEnglish(key: string): string {
  const v = key.split('.').reduce<unknown>(
    (acc, part) => (acc && typeof acc === 'object' ? (acc as Record<string, unknown>)[part] : undefined),
    en as unknown,
  )
  if (typeof v !== 'string') throw new Error(`missing locale key: ${key}`)
  return v
}

function fromRegistry(
  group: 'block' | 'insert', t: (key: string) => string,
): RichTextSlashItem[] {
  return RICH_TEXT_COMMANDS
    .filter((c) => c.group === group)
    .map((c) => ({
      id: c.id,
      label: t(c.labelKey),
      enLabel: resolveEnglish(c.labelKey).toLowerCase(),
      aliases: SLASH_ALIASES[c.id] ?? [],
      run: c.run,
    }))
}

export function buildSlashItems(t: (key: string) => string): RichTextSlashItem[] {
  return [
    ...HEADING_LEVELS.map((level) => ({
      id: `heading${level}`,
      label: t(`fields.richtext.heading${level}`),
      enLabel: resolveEnglish(`fields.richtext.heading${level}`).toLowerCase(),
      aliases: [`h${level}`, `heading${level}`],
      run: (editor: Editor) => { editor.chain().focus().setHeading({ level }).run() },
    })),
    ...fromRegistry('block', t),
    // The toolbar's table entry is a grid popover plus a custom-size dialog, not a single command,
    // so it never joined RICH_TEXT_COMMANDS -- it has to be hand-authored here too.
    {
      id: 'table',
      label: t('fields.richtext.table'),
      enLabel: resolveEnglish('fields.richtext.table').toLowerCase(),
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
    // No .toLowerCase() on the alias or enLabel side: SLASH_ALIASES is pinned lowercase ASCII by
    // richTextSlashCommands.test.ts, and enLabel is lowercased once in buildSlashItems -- both
    // already compare correctly against q (itself lowercased above) as-is.
    // Substring matching (not prefix, not exact) is intended here, the same as the label branch
    // below -- 'ul' matching both bulletList's own alias and hr's 'rule' alias is accepted
    // over-matching for a candidate list navigated with arrow keys, not a bug to eliminate.
    (item) => item.label.toLowerCase().includes(q) || item.enLabel.includes(q)
      || item.aliases.some((a) => a.includes(q)),
  )
}
