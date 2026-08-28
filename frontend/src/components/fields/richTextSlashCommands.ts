import type { Editor } from '@tiptap/vue-3'
import en from '../../locales/en'
import { RICH_TEXT_COMMANDS, type RichTextCommandContext } from './richTextCommands'
import { HEADING_LEVELS, type HeadingLevel } from './richTextHeadings'

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
// locale with zero entries here. Only some entries below add anything beyond that -- an
// abbreviation the label doesn't spell out (ol, pre, hr, img) or a synonym distinct from it
// (ordered, divider, picture); the rest (ul, bullet, list, number, quote, blockquote, code, rule,
// image) are already substrings of their own item's English label and match through enLabel
// regardless of whether they're listed here.
const SLASH_ALIASES: Readonly<Record<string, ReadonlyArray<string>>> = {
  bulletList: ['ul', 'bullet', 'list'],
  orderedList: ['ol', 'number', 'ordered'],
  blockquote: ['quote', 'blockquote'],
  codeBlock: ['code', 'pre'],
  hr: ['hr', 'divider', 'rule'],
  image: ['img', 'image', 'picture'],
}

const TABLE_LABEL_KEY = 'fields.richtext.table'

function headingLabelKey(level: HeadingLevel): string {
  return `fields.richtext.heading${level}`
}

// Resolves a dotted key against the English catalogue, throwing if it's missing there. Used only
// below, to build EN_LABELS once at module load -- never from inside buildSlashItems, which
// richTextSlashExtension.ts calls on every keystroke inside the suggestion plugin. A typo'd
// labelKey needs to fail the moment this module is imported, not mid-edit.
//
// A near-identical dotted-path resolver exists twice more, in richTextSlashCommands.test.ts and
// richTextCommands.test.ts -- none of the three has been promoted to lib/ yet, each small enough
// alone to carry its own copy. A fourth copy showing up would be the sign that call is wrong.
function resolveEnglish(key: string): string {
  const v = key.split('.').reduce<unknown>(
    (acc, part) => (acc && typeof acc === 'object' ? (acc as Record<string, unknown>)[part] : undefined),
    en as unknown,
  )
  if (typeof v !== 'string') throw new Error(`richTextSlashCommands.ts: "${key}" is missing from locales/en.ts`)
  return v
}

// Every labelKey the slash menu can ever use, resolved once here rather than lazily per item --
// see resolveEnglish above for why. Keyed by labelKey so fromRegistry and the two hand-authored
// call sites below (headings, table) can all share one lookup instead of each resolving their own.
const EN_LABELS: ReadonlyMap<string, string> = new Map([
  ...HEADING_LEVELS.map(
    (level) => [headingLabelKey(level), resolveEnglish(headingLabelKey(level)).toLowerCase()] as const,
  ),
  ...RICH_TEXT_COMMANDS.map((c) => [c.labelKey, resolveEnglish(c.labelKey).toLowerCase()] as const),
  [TABLE_LABEL_KEY, resolveEnglish(TABLE_LABEL_KEY).toLowerCase()] as const,
])

// Defensive, not expected to fire: every call site below passes a key built the same way EN_LABELS
// itself was (headingLabelKey, TABLE_LABEL_KEY, or a RICH_TEXT_COMMANDS entry's own labelKey), so
// this can only throw if a future edit lets those two sides drift apart.
function enLabelFor(key: string): string {
  const v = EN_LABELS.get(key)
  if (v === undefined) throw new Error(`richTextSlashCommands.ts: "${key}" was never resolved into EN_LABELS`)
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
      enLabel: enLabelFor(c.labelKey),
      aliases: SLASH_ALIASES[c.id] ?? [],
      run: c.run,
    }))
}

export function buildSlashItems(t: (key: string) => string): RichTextSlashItem[] {
  return [
    ...HEADING_LEVELS.map((level) => ({
      id: `heading${level}`,
      label: t(headingLabelKey(level)),
      enLabel: enLabelFor(headingLabelKey(level)),
      aliases: [`h${level}`, `heading${level}`],
      run: (editor: Editor) => { editor.chain().focus().setHeading({ level }).run() },
    })),
    ...fromRegistry('block', t),
    // The toolbar's table entry is a grid popover plus a custom-size dialog, not a single command,
    // so it never joined RICH_TEXT_COMMANDS -- it has to be hand-authored here too.
    {
      id: 'table',
      label: t(TABLE_LABEL_KEY),
      enLabel: enLabelFor(TABLE_LABEL_KEY),
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
    // richTextSlashCommands.test.ts, and enLabel is already lowercase by construction (EN_LABELS
    // above, also pinned by richTextSlashCommands.test.ts) -- both already compare correctly
    // against q (itself lowercased above) as-is.
    // Substring matching (not prefix, not exact) is intended here, the same as the label branch
    // below -- 'ul' matching both bulletList's own alias and hr's 'rule' alias is accepted
    // over-matching for a candidate list navigated with arrow keys, not a bug to eliminate.
    (item) => item.label.toLowerCase().includes(q) || item.enLabel.includes(q)
      || item.aliases.some((a) => a.includes(q)),
  )
}
