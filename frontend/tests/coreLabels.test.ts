// @vitest-environment node
// Reads the core schema snapshot from disk (see schemaContract.test.ts for why not under jsdom).
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, it, expect } from 'vitest'
import { catalogs } from '../src/locales'
import type { CoreLabelCatalog } from '../src/lib/localizeCoreSchema'
import type { CollectionMeta } from '../src/types/schema'

const SNAPSHOT_PATH = fileURLToPath(new URL('../../schema/core-collections.json', import.meta.url))
const collections: CollectionMeta[] = JSON.parse(readFileSync(SNAPSHOT_PATH, 'utf8'))

// Core texts zh-TW deliberately keeps identical to the English. Each entry is a `key` below.
const KEPT_IN_ENGLISH: readonly string[] = []

const AUDIT_FIELDS = new Set(['createdAt', 'createdBy', 'updatedAt', 'updatedBy'])

type Expected = { key: string; english: string }

function expectedTexts(): Expected[] {
  const out: Expected[] = []
  for (const c of collections) {
    if (c.group) out.push({ key: `groups.${c.group}`, english: c.group })
    out.push({ key: `collections.${c.name}.label`, english: c.label })
    for (const f of c.fields) {
      const key = f.isSystem && AUDIT_FIELDS.has(f.name) ? `audit.${f.name}` : `collections.${c.name}.fields.${f.name}`
      out.push({ key, english: f.label })
      for (const o of f.options ?? []) {
        out.push({ key: `collections.${c.name}.options.${f.name}.${o.value}`, english: o.label })
      }
    }
    for (const r of c.relations) out.push({ key: `collections.${c.name}.relations.${r.name}`, english: r.label })
  }
  return out
}

function lookup(catalog: CoreLabelCatalog, key: string): unknown {
  return key.split('.').reduce<unknown>(
    (node, part) => (node !== null && typeof node === 'object' && Object.hasOwn(node, part) ? (node as Record<string, unknown>)[part] : undefined),
    catalog,
  )
}

const texts = expectedTexts()

describe('core label catalogs match the core schema snapshot', () => {
  it('covers a non-empty snapshot', () => {
    expect(texts.length).toBeGreaterThan(0)
  })

  it.each(texts)('en holds the server English for $key', ({ key, english }) => {
    expect(lookup(catalogs.en.core, key)).toBe(english)
  })

  it.each(texts)('zh-TW translates $key', ({ key, english }) => {
    const value = lookup(catalogs['zh-TW'].core, key)
    expect(typeof value).toBe('string')
    expect((value as string).trim()).not.toBe('')
    if (!KEPT_IN_ENGLISH.includes(key)) expect(value).not.toBe(english)
  })

  it('holds no entry the snapshot lacks', () => {
    const known = new Set(texts.map((t) => t.key))
    for (const locale of ['en', 'zh-TW'] as const) {
      for (const key of leafKeys(catalogs[locale].core)) expect(known.has(key), `${locale}: ${key}`).toBe(true)
    }
  })
})

function leafKeys(node: unknown, prefix = ''): string[] {
  if (typeof node === 'string') return [prefix]
  if (node === null || typeof node !== 'object') return []
  return Object.entries(node).flatMap(([k, v]) => leafKeys(v, prefix ? `${prefix}.${k}` : k))
}
