// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { ICON_MAP } from '../src/lib/icons'

const SRC = fileURLToPath(new URL('../src/', import.meta.url))

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return entry === 'ui' ? [] : walk(full)
    return /\.(vue|ts)$/.test(entry) ? [full] : []
  })
}

describe('icon coverage', () => {
  // Guard against a half-done migration: if a template still says `pi pi-foo` and the map has
  // no `foo`, that icon silently degrades to a generic file with no error anywhere.
  // Scan the real source rather than trusting the map to describe itself.
  it('maps every pi- token still present in src/', () => {
    const files = walk(SRC).filter((f) => !/icons\.(ts|test\.ts)$/.test(f))
    const tokens = new Set<string>()
    for (const file of files) {
      for (const m of readFileSync(file, 'utf8').matchAll(/\bpi-([a-z0-9-]+)/g)) tokens.add(m[1])
    }
    const unmapped = [...tokens].filter((tok) => !(tok in ICON_MAP))
    expect(unmapped, `ICON_MAP is missing: ${unmapped.join(', ')}`).toEqual([])
  })
})
