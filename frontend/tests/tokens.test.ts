// @vitest-environment node
// Under the jsdom environment Vitest transforms this file in Vite's client mode, where
// `new URL(<literal>, import.meta.url)` is rewritten twice — Vite swaps the literal for a
// dev-server path (/@fs/…) and Vitest's normalize-url plugin swaps the base for self.location —
// so the result is http://localhost:3000/@fs/… and fileURLToPath rejects it. Neither rewrite
// happens in the node environment (ssr transform), which needs no DOM anyway. Same fix as
// tests/schemaContract.test.ts, which lives here for the same reason: tests that read files
// from disk live in frontend/tests/ (tsconfig.test.json, which declares "node" in its types),
// not under src/ (tsconfig.app.json, which is the app program and has no Node globals).
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const css = readFileSync(fileURLToPath(new URL('../src/assets/tokens.css', import.meta.url)), 'utf8')

function block(selector: string): string {
  // Non-greedy up to the first closing brace — every block below is flat (no nesting).
  const m = new RegExp(`${selector.replace('.', '\\.')}\\s*\\{([^}]*)\\}`).exec(css)
  if (!m) throw new Error(`no ${selector} block in tokens.css`)
  return m[1]
}

const SLOTS = [
  'background', 'foreground', 'card', 'card-foreground', 'popover', 'popover-foreground',
  'primary', 'primary-foreground', 'secondary', 'secondary-foreground',
  'muted', 'muted-foreground', 'accent', 'accent-foreground',
  'destructive', 'border', 'input', 'ring', 'success', 'warning',
  'sidebar', 'sidebar-foreground', 'sidebar-primary', 'sidebar-primary-foreground',
  'sidebar-accent', 'sidebar-accent-foreground', 'sidebar-border', 'sidebar-ring',
]

describe('tokens.css', () => {
  it('defines every semantic slot in both colour schemes', () => {
    const light = block(':root')
    const dark = block('.app-dark')
    for (const slot of SLOTS) {
      expect(light, `:root is missing --${slot}`).toContain(`--${slot}:`)
      expect(dark, `.app-dark is missing --${slot}`).toContain(`--${slot}:`)
    }
  })

  // The single most dangerous mistake in this migration: shadcn's --accent is the
  // hover/active surface, NOT the brand colour. Brand sky belongs to --primary.
  it('binds brand sky to --primary and leaves --accent as a neutral hover surface', () => {
    expect(block(':root')).toContain('--primary: #0284c7')
    expect(block('.app-dark')).toContain('--primary: #38bdf8')
    expect(block(':root')).not.toContain('--accent: #0284c7')
    expect(block('.app-dark')).not.toContain('--accent: #38bdf8')
  })

  it('uses the intended dark zinc values, not a lighter/darker alternate', () => {
    const dark = block('.app-dark')
    expect(dark).toContain('--background: #18181b')  // zinc-900, NOT #09090b
    expect(dark).toContain('--border: #3f3f46')      // zinc-700, NOT #27272a
  })

  // Against #18181b, :root's --destructive (#dc2626) measures 3.67:1 — below WCAG AA's
  // 4.5:1 for text. --warning (#d97706) already clears AA here (5.56:1) on its own; its
  // lighter dark value matches theme.css's dark --warn instead, for border/future-text
  // headroom, not because :root's value failed anything. Either way .app-dark must not
  // repeat the :root values verbatim.
  it('lightens --destructive and --warning for dark-surface contrast, unlike :root', () => {
    const light = block(':root')
    const dark = block('.app-dark')
    expect(light).toContain('--destructive: #dc2626')
    expect(light).toContain('--warning: #d97706')
    expect(dark).toContain('--destructive: #f87171')
    expect(dark).toContain('--warning: #fbbf24')
    expect(light).not.toContain('--destructive: #f87171')
    expect(light).not.toContain('--warning: #fbbf24')
  })

  it('drives dark mode off .app-dark, not shadcn default .dark', () => {
    expect(css).toContain('@custom-variant dark (&:is(.app-dark *))')
  })

  // 0.625rem = 10px -> radius-sm 6px (buttons/inputs), radius-md 8px (cards/dialogs).
  it('sets the radius base so sm=6px and md=8px fall out of the scale', () => {
    expect(block(':root')).toContain('--radius: 0.625rem')
    expect(css).toContain('--radius-sm: calc(var(--radius) * 0.6)')
    expect(css).toContain('--radius-md: calc(var(--radius) * 0.8)')
  })

  it('exposes every slot to Tailwind through @theme inline', () => {
    const theme = block('@theme inline')
    for (const slot of SLOTS) {
      expect(theme, `@theme inline is missing --color-${slot}`).toContain(`--color-${slot}: var(--${slot})`)
    }
  })

  // 12 vendored components use animate-in / slide-in-from-* / zoom-* utilities that only exist
  // if this plugin is imported. Tailwind v4 emits nothing for an undefined utility, so losing
  // this import is silent — hence a test rather than a comment.
  it('imports tw-animate-css so the vendored components\' transitions exist', () => {
    expect(css).toContain('@import "tw-animate-css"')
  })
})
