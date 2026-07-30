// @vitest-environment node
// Under the jsdom environment Vitest transforms this file in Vite's client mode, where
// `new URL(<literal>, import.meta.url)` is rewritten twice — Vite swaps the literal for a
// dev-server path (/@fs/…) and Vitest's normalize-url plugin swaps the base for self.location —
// so the result is http://localhost:3000/@fs/… and fileURLToPath rejects it. Neither rewrite
// happens in the node environment (ssr transform), which needs no DOM anyway.
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, it, expect } from 'vitest'
import { registry } from '../src/lib/fieldTypes/registry'
import { ALL_FIELD_INTERFACES } from '../src/lib/fieldTypes/types'
import { MAPPED_RELATION_INTERFACES } from '../src/lib/relationInputKind'
import { selectListColumns } from '../src/lib/selectListColumns'
import type { CollectionMeta, FieldMeta } from '../src/types/schema'

// The backend half of this contract is tests/Struo.Tests/Api/CoreSchemaSnapshotTests.cs, which
// regenerates both files below. Read them from disk rather than importing them, so a missing file
// fails with a clear path instead of a module-resolution error.
const SNAPSHOT_PATH = fileURLToPath(new URL('../../schema/core-collections.json', import.meta.url))
const INTERFACES_PATH = fileURLToPath(new URL('../../schema/interfaces.json', import.meta.url))

const collections: CollectionMeta[] = JSON.parse(readFileSync(SNAPSHOT_PATH, 'utf8'))

// Every member of the backend's two interface enums, in the same camelCase form the wire uses.
// Unlike core-collections.json this does not depend on any collection USING an interface, which is
// what makes the assertions below unconditional.
const interfaces: { fieldInterfaces: string[]; relationInterfaces: string[] } =
  JSON.parse(readFileSync(INTERFACES_PATH, 'utf8'))

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

// The assertions in the block above are conditional: they only see an interface that some core
// collection actually uses, and new field types are typically introduced for content collections
// rather than for the seven framework tables. The block below closes that gap by checking the backend
// enums directly, so a new member is caught the moment it is declared. Both enums are checked in both
// directions — a backend member the frontend has not mirrored, and a frontend entry left behind after
// a backend member was removed.
describe('schema contract: the frontend covers every declared backend interface', () => {
  it('reads non-empty interface lists', () => {
    expect(interfaces.fieldInterfaces.length).toBeGreaterThan(0)
    expect(interfaces.relationInterfaces.length).toBeGreaterThan(0)
  })

  it('declares every backend FieldInterface member in ALL_FIELD_INTERFACES', () => {
    const known = new Set<string>(ALL_FIELD_INTERFACES)
    const missing = interfaces.fieldInterfaces.filter((i) => !known.has(i))

    expect(
      missing,
      'backend FieldInterface members absent from frontend ALL_FIELD_INTERFACES',
    ).toEqual([])
  })

  it('gives every backend FieldInterface member its own registry entry', () => {
    // Key presence, not object identity — see the note on the equivalent assertion above.
    const missing = interfaces.fieldInterfaces.filter(
      (i) => !Object.prototype.hasOwnProperty.call(registry, i))

    expect(
      missing,
      'backend FieldInterface members with no registry key (these fall through to the read-only renderer)',
    ).toEqual([])
  })

  it('lists no field interface the backend no longer declares', () => {
    // The reverse direction: catches a frontend entry left behind after a backend member is removed.
    const declared = new Set(interfaces.fieldInterfaces)
    const stale = ALL_FIELD_INTERFACES.filter((i) => !declared.has(i))

    expect(stale, 'frontend ALL_FIELD_INTERFACES entries the backend enum no longer declares').toEqual([])
  })

  it('maps every backend RelationInterface member to a real relation input', () => {
    // Key presence, for the same reason as the registry check: relationInputKind() returns the legal
    // value 'readonly' for anything absent from its map, so a member deliberately mapped to
    // 'readonly' would be indistinguishable from one that fell through.
    const mapped = new Set(MAPPED_RELATION_INTERFACES)
    const unmapped = interfaces.relationInterfaces.filter((i) => !mapped.has(i))

    expect(
      unmapped,
      'backend RelationInterface members absent from relationInputKind\'s map (these render silently read-only)',
    ).toEqual([])
  })

  it('maps no relation interface the backend no longer declares', () => {
    const declared = new Set(interfaces.relationInterfaces)
    const stale = MAPPED_RELATION_INTERFACES.filter((i) => !declared.has(i))

    expect(stale, 'relationInputKind map entries the backend enum no longer declares').toEqual([])
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
