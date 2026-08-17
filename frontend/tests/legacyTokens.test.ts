// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const SRC = fileURLToPath(new URL('../src/', import.meta.url))

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) return entry === 'ui' ? [] : walk(full)
    return /\.(vue|css)$/.test(entry) ? [full] : []
  })
}

const files = walk(SRC).filter((f) => !f.endsWith('tokens.css'))

// tokens.css owns the shadcn semantic names. theme.css is LATER in main.ts's import order and
// carries equal specificity, so every name it re-declares silently wins for the whole document
// — including --accent (which would render brand sky as every hover surface) and --radius
// (which would make rounded-sm 4.8px instead of the contracted 6px). --radius-lg was measured
// (Step 0) to also be emitted into :root by Tailwind v4's `@theme inline`, so it collides too.
const RENAMED = ['accent', 'muted', 'radius', 'radius-lg'] as const   // meaning differs -> theme.css declares none of them
const SURRENDERED = ['border', 'success'] as const        // meaning matches -> tokens.css owns it

describe('legacy token collision', () => {
  it('no file outside tokens.css consumes the renamed names', () => {
    const pattern = new RegExp(RENAMED.map((n) => `var\\(--${n}[,)]`).join('|'))
    const offenders = files.filter((f) => pattern.test(readFileSync(f, 'utf8')))
    expect(offenders.map((f) => f.replace(SRC, ''))).toEqual([])
  })

  it('theme.css no longer declares any colliding name', () => {
    const css = readFileSync(join(SRC, 'assets/theme.css'), 'utf8')
    for (const name of [...RENAMED, ...SURRENDERED]) {
      expect(css, `theme.css still declares --${name}`).not.toMatch(new RegExp(`^\\s*--${name}:`, 'm'))
    }
  })

  it('no --legacy-* token is declared or consumed anywhere', () => {
    const css = readFileSync(join(SRC, 'assets/theme.css'), 'utf8')
    expect([...css.matchAll(/--legacy-[a-z-]+:/g)].map((m) => m[0])).toEqual([])
    const consumers = files.filter((f) => /var\(--legacy-/.test(readFileSync(f, 'utf8')))
    expect(consumers.map((f) => f.replace(SRC, ''))).toEqual([])
  })
})
