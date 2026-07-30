// This test needs no DOM; it also must run under Node's own URL implementation. Under the
// project's default jsdom environment, the global URL constructor resolves relative refs against
// jsdom's document location (http://localhost:3000/) instead of a file:// base, which breaks
// fileURLToPath below. Node environment sidesteps that without touching the shared vite.config.ts.
// @vitest-environment node
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, it, expect } from 'vitest'
import { registry } from '../src/lib/fieldTypes/registry'
import { ALL_FIELD_INTERFACES } from '../src/lib/fieldTypes/types'
import { selectListColumns } from '../src/lib/selectListColumns'
import type { CollectionMeta, FieldMeta } from '../src/types/schema'

// The backend half of this contract is tests/Struo.Tests/Api/CoreSchemaSnapshotTests.cs, which
// regenerates this file from the real GET /api/schema response. Read it from disk rather than
// importing it, so a missing file fails with a clear path instead of a module-resolution error.
const SNAPSHOT_PATH = fileURLToPath(new URL('../../schema/core-collections.json', import.meta.url))

const collections: CollectionMeta[] = JSON.parse(readFileSync(SNAPSHOT_PATH, 'utf8'))

// A Repeater field carries a nested sub-field schema, and the registry renders both levels,
// so both levels are part of the contract.
function allFields(fields: FieldMeta[]): FieldMeta[] {
  return fields.flatMap((f) => [f, ...(f.fields ? allFields(f.fields) : [])])
}

// Keys the frontend types declare as non-optional. The basis is deliberately the FRONTEND type,
// not the backend record's `required` keyword: extra keys the backend sends (fieldGroups,
// translation) are harmless, whereas a renamed or dropped key the SPA reads is a break.
const REQUIRED_COLLECTION_KEYS: readonly (keyof CollectionMeta)[] =
  ['name', 'label', 'fields', 'relations'] as const
const REQUIRED_FIELD_KEYS: readonly (keyof FieldMeta)[] = [
  'name', 'label', 'interface', 'required', 'searchable', 'sortable',
  'readOnly', 'hidden', 'translatable', 'sort', 'isSystem',
] as const

describe('schema contract: core collections vs the field-type registry', () => {
  it('reads a non-empty snapshot', () => {
    expect(collections.length).toBeGreaterThan(0)
  })

  it('declares every field interface in ALL_FIELD_INTERFACES', () => {
    const known = new Set<string>(ALL_FIELD_INTERFACES)
    const unknown = collections.flatMap((c) =>
      allFields(c.fields ?? [])
        .filter((f) => !known.has(f.interface))
        .map((f) => `${c.name}.${f.name} -> "${f.interface}"`))

    expect(
      unknown,
      'backend FieldInterface members absent from frontend ALL_FIELD_INTERFACES',
    ).toEqual([])
  })

  it('gives every field interface its own registry entry, not the read-only fallback', () => {
    // Key presence, NOT object identity. `readonlyDef` is one shared object used as the value of
    // registry.hidden, the value of registry.uuid, AND getFieldType()'s fallback return — so
    // registry.uuid === getFieldType('anythingUnknown'), and identity cannot tell a legitimate
    // read-only mapping apart from a silent fallback.
    const missing = collections.flatMap((c) =>
      allFields(c.fields ?? [])
        .filter((f) => !Object.prototype.hasOwnProperty.call(registry, f.interface))
        .map((f) => `${c.name}.${f.name} -> "${f.interface}"`))

    expect(
      missing,
      'field interfaces with no dedicated registry component (these render silently read-only)',
    ).toEqual([])
  })

  it('carries every key the frontend types declare non-optional', () => {
    const gaps: string[] = []
    for (const c of collections) {
      for (const k of REQUIRED_COLLECTION_KEYS) {
        if (!(k in c)) gaps.push(`collection "${c.name}" is missing "${k}"`)
      }
      for (const f of allFields(c.fields ?? [])) {
        for (const k of REQUIRED_FIELD_KEYS) {
          if (!(k in f)) gaps.push(`${c.name}.${f.name} is missing "${k}"`)
        }
      }
    }

    expect(gaps, 'backend DTO keys the frontend types require are absent').toEqual([])
  })
})

// A3/A4. These drive the real production selector rather than re-implementing its rule
// (`!isSystem && !hidden && getFieldType(interface).listColumn !== null`, then defaultDisplayField
// first, then the first six). A local copy of that rule would drift from it; calling it means the
// assertion always reflects what the admin list view actually renders.
describe('schema contract: the admin list view can render every core collection', () => {
  it.each(collections.map((c) => [c.name, c] as const))(
    'collection "%s" yields at least one list column',
    (_name, collection) => {
      expect(selectListColumns(collection).length).toBeGreaterThan(0)
    },
  )

  const withDisplayField = collections.filter((c) => !!c.defaultDisplayField)

  it.each(withDisplayField.map((c) => [c.name, c] as const))(
    'collection "%s" keeps its defaultDisplayField as a list column',
    (_name, collection) => {
      expect(selectListColumns(collection).map((col) => col.field))
        .toContain(collection.defaultDisplayField)
    },
  )
})
