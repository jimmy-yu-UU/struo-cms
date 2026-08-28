import { describe, it, expect, vi } from 'vitest'
import en from '../../locales/en'
import zhTW from '../../locales/zh-TW'
import { RICH_TEXT_COMMANDS } from './richTextCommands'
import { buildSlashItems, filterSlashItems, type RichTextSlashItem } from './richTextSlashCommands'

function resolveFrom(dict: unknown): (key: string) => string {
  return (key) => {
    const v = key.split('.').reduce<unknown>(
      (acc, part) => (acc && typeof acc === 'object' ? (acc as Record<string, unknown>)[part] : undefined),
      dict,
    )
    if (typeof v !== 'string') throw new Error(`missing locale key: ${key}`)
    return v
  }
}

const resolve = resolveFrom(en)

const items = buildSlashItems(resolve)

// Maps a built item's id back to the labelKey buildSlashItems resolved it from, so a test can read
// an item's true English label straight from the English catalogue -- independently of the item's
// own enLabel field, which is what's under test below and so cannot be trusted as the source of it.
function labelKeyFor(id: string): string {
  const heading = /^heading(\d)$/.exec(id)
  if (heading) return `fields.richtext.heading${heading[1]}`
  if (id === 'table') return 'fields.richtext.table'
  const command = RICH_TEXT_COMMANDS.find((c) => c.id === id)
  if (!command) throw new Error(`no labelKey mapping for slash item id: ${id}`)
  return command.labelKey
}

describe('buildSlashItems', () => {
  it('lists headings, then block conversions, then the inserts', () => {
    expect(items.map((i) => i.id)).toEqual([
      'heading2', 'heading3', 'heading4', 'heading5', 'heading6',
      'bulletList', 'orderedList', 'blockquote', 'codeBlock',
      'table', 'hr', 'image',
    ])
  })

  it('resolves every label through the translator', () => {
    expect(items.find((i) => i.id === 'heading2')?.label).toBe('Heading 2')
    expect(items.find((i) => i.id === 'table')?.label).toBe('Table')
    expect(items.every((i) => i.label.length > 0)).toBe(true)
  })
})

describe('filterSlashItems', () => {
  it('returns everything for an empty query', () => {
    expect(filterSlashItems(items, '')).toHaveLength(items.length)
    expect(filterSlashItems(items, '   ')).toHaveLength(items.length)
  })

  it('matches an alias', () => {
    expect(filterSlashItems(items, 'h3').map((i) => i.id)).toEqual(['heading3'])
    // Not a full-equality check: 'ul' is also a genuine substring of hr's own 'rule' alias (and of
    // hr's "Horizontal rule" label), so both items legitimately match. What matters for a
    // candidate list is that the item the alias exists for ranks first.
    expect(filterSlashItems(items, 'ul')[0]?.id).toBe('bulletList')
  })

  // 'ockquote' is a strict interior substring of blockquote's own label and alias (neither starts
  // with it), so a prefix-only implementation finds nothing here and this test fails.
  it('matches a substring, not only a prefix', () => {
    expect(filterSlashItems(items, 'ockquote').map((i) => i.id)).toEqual(['blockquote'])
  })

  // 'insert' appears only inside image's label ("Insert image") and in no item's alias list, so
  // this only passes if filterSlashItems' label branch runs -- every other test in this file
  // happens to resolve through the alias branch instead.
  it('matches on label text alone, with no alias involved', () => {
    expect(filterSlashItems(items, 'insert').map((i) => i.id)).toEqual(['image'])
  })

  // Reuses the same alias-free word as above, uppercased, so this pins the label branch's own
  // case-insensitivity specifically -- 'BLOCKQUOTE' would have resolved through the alias
  // 'blockquote' instead and proven nothing about the label branch.
  it('matches the translated label, case-insensitively', () => {
    expect(filterSlashItems(items, 'INSERT').map((i) => i.id)).toEqual(['image'])
  })

  // The whole point of the alias table: a zh-TW operator has just typed "/" and the IME is still
  // in ASCII, so the translated labels are unreachable.
  it('still finds items by alias when the labels are translated', () => {
    const zh = buildSlashItems((key) => (key === 'fields.richtext.table' ? '表格' : key))
    expect(filterSlashItems(zh, 'table').map((i) => i.id)).toEqual(['table'])
  })

  it('reaches an item through its English label alone, with an empty alias list', () => {
    const synthetic: RichTextSlashItem = {
      id: 'synthetic', label: '合成指令', enLabel: 'synthetic command', aliases: [], run: () => {},
    }
    expect(filterSlashItems([synthetic], 'synthetic').map((i) => i.id)).toEqual(['synthetic'])
  })

  it('keeps enLabel lowercase', () => {
    for (const item of items) expect(item.enLabel, item.id).toBe(item.enLabel.toLowerCase())
  })

  // An upper-case query would never match an alias that wasn't already lowercase.
  it('keeps every alias lowercase ASCII', () => {
    for (const item of items) {
      for (const alias of item.aliases) expect(alias, `${item.id}/${alias}`).toMatch(/^[a-z0-9]+$/)
    }
  })

  it('returns an empty list when nothing matches', () => {
    expect(filterSlashItems(items, 'zzzz')).toEqual([])
  })
})

// The describe block above resolves every item's translated label through the English bundle, a
// synthetic stand-in, or the label-of-one stub above -- never the zh-TW catalogue -- so a query
// that only matched because the translated label happened to carry ASCII would still pass there.
// These build from zh-TW instead, the locale the defect was found under.
describe('filterSlashItems (zh-TW build)', () => {
  const zhItems = buildSlashItems(resolveFrom(zhTW))

  it.each([
    'horizontal', 'numbered', 'insert', 'list', 'block',
  ])('/%s reaches the same items as it does when built from English', (query) => {
    expect(filterSlashItems(zhItems, query).map((i) => i.id))
      .toEqual(filterSlashItems(items, query).map((i) => i.id))
  })

  // /list and /block are the sharper case: unfixed, they returned one matching item instead of an
  // empty list, which reads as a working (if short) menu rather than a broken one.
  it('reaches both list commands, not only the one carrying a matching alias', () => {
    expect(filterSlashItems(zhItems, 'list').map((i) => i.id)).toEqual(['bulletList', 'orderedList'])
  })

  it('reaches both block commands, not only the one carrying a matching alias', () => {
    expect(filterSlashItems(zhItems, 'block').map((i) => i.id)).toEqual(['blockquote', 'codeBlock'])
  })

  // The query here is read from the English catalogue directly (labelKeyFor + resolve), not from
  // the item's own enLabel -- deriving it from enLabel itself could never catch enLabel being
  // computed wrong, since a wrong enLabel and a query derived from that same wrong value would
  // still agree with each other. This is what actually protects a future registry or hand-authored
  // entry whose enLabel silently stops being the English catalogue (translated by mistake, say).
  it('reaches every item through the full text of its own English label', () => {
    for (const item of zhItems) {
      const query = resolve(labelKeyFor(item.id)).toLowerCase()
      expect(filterSlashItems(zhItems, query).map((i) => i.id), item.id).toContain(item.id)
    }
  })
})

describe('item commands', () => {
  it('sets a heading at the level its id names', () => {
    const run = vi.fn()
    const chain = { focus: () => chain, setHeading: vi.fn(() => chain), run }
    const editor = { chain: () => chain } as never
    items.find((i) => i.id === 'heading4')!.run(editor, {} as never)
    expect(chain.setHeading).toHaveBeenCalledWith({ level: 4 })
    expect(run).toHaveBeenCalled()
  })

  it('inserts a 3x3 table with a header row', () => {
    const run = vi.fn()
    const chain = { focus: () => chain, insertTable: vi.fn(() => chain), run }
    const editor = { chain: () => chain } as never
    items.find((i) => i.id === 'table')!.run(editor, {} as never)
    expect(chain.insertTable).toHaveBeenCalledWith({ rows: 3, cols: 3, withHeaderRow: true })
    expect(run).toHaveBeenCalled()
  })

  it('inserts a horizontal rule', () => {
    const run = vi.fn()
    const chain = { focus: () => chain, setHorizontalRule: vi.fn(() => chain), run }
    const editor = { chain: () => chain } as never
    items.find((i) => i.id === 'hr')!.run(editor, {} as never)
    expect(chain.setHorizontalRule).toHaveBeenCalled()
    expect(run).toHaveBeenCalled()
  })

  it('reuses the registry command for an item derived from it', () => {
    const ctx = { openImageDialog: vi.fn(), openLinkDialog: vi.fn() }
    items.find((i) => i.id === 'image')!.run({} as never, ctx as never)
    expect(ctx.openImageDialog).toHaveBeenCalledTimes(1)
    expect(items.find((i) => i.id === 'image')!.run)
      .toBe(RICH_TEXT_COMMANDS.find((c) => c.id === 'image')!.run)
  })
})
