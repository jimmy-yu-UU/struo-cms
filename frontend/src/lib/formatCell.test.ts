import { describe, it, expect } from 'vitest'
import { formatCell } from './formatCell'
import type { FieldMeta } from '../types/schema'

function field(partial: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return {
    name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false,
    ...partial,
  } as FieldMeta
}

describe('formatCell', () => {
  it('renders em-dash for null/undefined/empty', () => {
    const f = field({ interface: 'text' })
    expect(formatCell(null, f)).toBe('—')
    expect(formatCell(undefined, f)).toBe('—')
    expect(formatCell('', f)).toBe('—')
  })

  it('maps a select value to its option label, falling back to the raw value', () => {
    const f = field({ interface: 'select', options: [{ value: 'draft', label: 'Draft' }] })
    expect(formatCell('draft', f)).toBe('Draft')
    expect(formatCell('unknown', f)).toBe('unknown')
  })

  it('renders booleans as Yes/No', () => {
    const f = field({ interface: 'boolean' })
    expect(formatCell(true, f)).toBe('Yes')
    expect(formatCell(false, f)).toBe('No')
  })

  it('formats dateTime values and passes plain text through', () => {
    expect(formatCell('2026-01-02T03:04:05Z', field({ interface: 'dateTime' }))).toContain('2026')
    expect(formatCell('not-a-date', field({ interface: 'dateTime' }))).toBe('not-a-date')
    expect(formatCell('hello', field({ interface: 'text' }))).toBe('hello')
  })

  // A Text-interface column falls back to asString's listColumn, whose value is typed unknown --
  // an object value must render as JSON, never the default Object.prototype.toString result.
  it('renders an object value as JSON rather than [object Object]', () => {
    expect(formatCell({ a: 1 }, field({ interface: 'text' }))).toBe('{"a":1}')
  })
})
