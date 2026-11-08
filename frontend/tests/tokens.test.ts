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

// WCAG 2.x relative-luminance contrast, plus the one colour-mix form --sidebar uses.
type Rgb = readonly [number, number, number]

const toLinear = (c: number): number => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4)
const toGamma = (c: number): number => (c <= 0.0031308 ? 12.92 * c : 1.055 * c ** (1 / 2.4) - 0.055)

function hexToRgb(hex: string): Rgb {
  const n = (i: number): number => parseInt(hex.slice(i, i + 2), 16) / 255
  return [n(1), n(3), n(5)]
}

function luminance([r, g, b]: Rgb): number {
  return 0.2126 * toLinear(r) + 0.7152 * toLinear(g) + 0.0722 * toLinear(b)
}

function contrast(a: Rgb, b: Rgb): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}

function toOklab([r, g, b]: Rgb): Rgb {
  const [lr, lg, lb] = [toLinear(r), toLinear(g), toLinear(b)]
  const l = Math.cbrt(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb)
  const m = Math.cbrt(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb)
  const s = Math.cbrt(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb)
  return [
    0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s,
  ]
}

function fromOklab([L, a, b]: Rgb): Rgb {
  const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
  const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
  const s = (L - 0.0894841775 * a - 1.291485548 * b) ** 3
  const lin = [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ]
  const [r, g, bl] = lin.map((c) => Math.min(1, Math.max(0, toGamma(c))))
  return [r, g, bl]
}

// `color-mix(in oklab, <fg> <pct>%, <bg>)`
function mixOklab(fg: string, pct: number, bg: string): Rgb {
  const f = toOklab(hexToRgb(fg))
  const b = toOklab(hexToRgb(bg))
  const w = pct / 100
  return fromOklab([f[0] * w + b[0] * (1 - w), f[1] * w + b[1] * (1 - w), f[2] * w + b[2] * (1 - w)])
}

function tokenHex(selector: string, name: string): string {
  const m = new RegExp(`--${name}:\\s*(#[0-9a-fA-F]{6})`).exec(block(selector))
  if (!m) throw new Error(`no hex --${name} in ${selector}`)
  return m[1]
}

// `--sidebar-accent`: `color-mix(in oklab, <fg>, transparent <pct>%)`, drawn over the sidebar surface.
// Browsers blend the resulting translucent colour with the backdrop in sRGB.
function sidebarAccentSurface(): Rgb {
  const m = /--sidebar-accent:\s*color-mix\(in oklab,\s*(#[0-9a-fA-F]{6}),\s*transparent\s+(\d+)%\)/.exec(block(':root'))
  if (!m) throw new Error('--sidebar-accent is no longer a transparent colour-mix')
  const alpha = 1 - Number(m[2]) / 100
  const fg = hexToRgb(m[1])
  const base = sidebarSurface()
  return [0, 1, 2].map((i) => fg[i] * alpha + base[i] * (1 - alpha)) as unknown as Rgb
}

function sidebarSurface(): Rgb {
  const m = /--sidebar:\s*color-mix\(in oklab,\s*(#[0-9a-fA-F]{6})\s+(\d+)%,\s*(#[0-9a-fA-F]{6})\)/.exec(block(':root'))
  if (!m) throw new Error('--sidebar is no longer a two-colour oklab mix')
  return mixOklab(m[1], Number(m[2]), m[3])
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

  // The single most dangerous mistake here: shadcn's --accent is the
  // hover/active surface, NOT the brand colour. Brand sky belongs to --primary.
  it('binds brand sky to --primary and leaves --accent as a neutral hover surface', () => {
    expect(block(':root')).toContain('--primary: #0369a1')
    expect(block('.app-dark')).toContain('--primary: #38bdf8')
    expect(block(':root')).not.toContain('--accent: #0369a1')
    expect(block('.app-dark')).not.toContain('--accent: #38bdf8')
  })

  // --accent used to be a 5% colour-mix wash of the foreground colour — nearly invisible as a
  // hover surface. Pin it to the same opaque value as --muted so a future edit can't silently
  // drift it back toward transparency.
  it('makes --accent an opaque surface matching --muted, not a translucent wash', () => {
    // --sidebar-accent is intentionally out of scope and still a color-mix wash, so this checks
    // the --accent declaration itself rather than the whole block for the string "color-mix".
    expect(block(':root')).toContain('--accent: #f4f4f5')
    expect(block('.app-dark')).toContain('--accent: #27272a')
    expect(block(':root')).not.toMatch(/--accent:\s*color-mix/)
    expect(block('.app-dark')).not.toMatch(/--accent:\s*color-mix/)
  })


  // WCAG AA text contrast (4.5:1) for the light scheme: white on the brand fill, and muted text
  // on every surface it appears on.
  it('keeps light-scheme brand and muted text at AA contrast', () => {
    const white = hexToRgb('#ffffff')
    expect(contrast(white, hexToRgb(tokenHex(':root', 'primary')))).toBeGreaterThanOrEqual(4.5)
    expect(contrast(white, hexToRgb(tokenHex(':root', 'sidebar-primary')))).toBeGreaterThanOrEqual(4.5)

    const muted = hexToRgb(tokenHex(':root', 'muted-foreground'))
    const surfaces: Record<string, Rgb> = {
      background: hexToRgb(tokenHex(':root', 'background')),
      card: hexToRgb(tokenHex(':root', 'card')),
      muted: hexToRgb(tokenHex(':root', 'muted')),
      accent: hexToRgb(tokenHex(':root', 'accent')),
      sidebar: sidebarSurface(),
      'sidebar-accent': sidebarAccentSurface(),
    }
    for (const [name, rgb] of Object.entries(surfaces)) {
      expect(contrast(muted, rgb), `muted-foreground on ${name}`).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('leaves the dark scheme brand and muted values unchanged', () => {
    expect(block('.app-dark')).toContain('--muted-foreground: #a1a1aa')
    expect(block('.app-dark')).toContain('--primary-foreground: #082f49')
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
