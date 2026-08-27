import { describe, it, expect, vi } from 'vitest'
import en from '../../locales/en'
import { buildSlashItems, filterSlashItems } from './richTextSlashCommands'

function resolve(key: string): string {
  const v = key.split('.').reduce<unknown>(
    (acc, part) => (acc && typeof acc === 'object' ? (acc as Record<string, unknown>)[part] : undefined),
    en as unknown,
  )
  if (typeof v !== 'string') throw new Error(`missing locale key: ${key}`)
  return v
}

const items = buildSlashItems(resolve)

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

  // A block/insert command added to the registry with no alias entry would silently arrive here
  // unreachable by any ASCII query, which is the only way a zh-TW user reaches it.
  it('gives every item at least one alias', () => {
    for (const item of items) expect(item.aliases.length, item.id).toBeGreaterThan(0)
  })

  it('keeps every alias lowercase ASCII', () => {
    for (const item of items) {
      for (const alias of item.aliases) expect(alias, `${item.id}/${alias}`).toMatch(/^[a-z0-9]+$/)
    }
  })
})

describe('filterSlashItems', () => {
  it('returns everything for an empty query', () => {
    expect(filterSlashItems(items, '')).toHaveLength(items.length)
    expect(filterSlashItems(items, '   ')).toHaveLength(items.length)
  })

  it('matches an alias', () => {
    expect(filterSlashItems(items, 'h3').map((i) => i.id)).toEqual(['heading3'])
    expect(filterSlashItems(items, 'ul').map((i) => i.id)).toEqual(['bulletList'])
  })

  it('matches the translated label, case-insensitively', () => {
    expect(filterSlashItems(items, 'BLOCKQUOTE').map((i) => i.id)).toEqual(['blockquote'])
  })

  it('matches a substring, not only a prefix', () => {
    expect(filterSlashItems(items, 'quote').map((i) => i.id)).toEqual(['blockquote'])
  })

  // The whole point of the alias table: a zh-TW operator has just typed "/" and the IME is still
  // in ASCII, so the translated labels are unreachable.
  it('still finds items by alias when the labels are translated', () => {
    const zh = buildSlashItems((key) => (key === 'fields.richtext.table' ? '表格' : key))
    expect(filterSlashItems(zh, 'table').map((i) => i.id)).toEqual(['table'])
  })

  it('returns an empty list when nothing matches', () => {
    expect(filterSlashItems(items, 'zzzz')).toEqual([])
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
  })

  it('reuses the registry command for an item derived from it', () => {
    const ctx = { openImageDialog: vi.fn(), openLinkDialog: vi.fn() }
    items.find((i) => i.id === 'image')!.run({} as never, ctx as never)
    expect(ctx.openImageDialog).toHaveBeenCalledTimes(1)
  })
})
