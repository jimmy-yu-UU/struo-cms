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
    // Test/spec files are not shipped surface — a selector string inside an assertion (e.g.
    // one confirming an icon is *absent*) is not a requirement that the icon exist.
    if (/\.(test|spec)\.ts$/.test(entry)) return []
    return /\.(vue|ts)$/.test(entry) ? [full] : []
  })
}

// A captured token ending in "-" is the static remainder of a dynamic class the scanner can't
// evaluate (e.g. `` `pi-align-${direction}` `` leaves behind "align-"). There is no exact key for
// that literal string to satisfy — instead at least one real ICON_MAP key must extend that
// prefix, so the map is proven to cover whatever the interpolation produces at runtime.
function isCovered(token: string, map: Record<string, unknown>): boolean {
  if (token.endsWith('-')) return Object.keys(map).some((key) => key.startsWith(token))
  return Object.hasOwn(map, token)
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
    const unmapped = [...tokens].filter((tok) => !isCovered(tok, ICON_MAP))
    expect(unmapped, `ICON_MAP is missing: ${unmapped.join(', ')}`).toEqual([])
  })
})
