// @vitest-environment node
// A disabled control shows the default cursor everywhere in the admin; there is no `not-allowed`
// cursor. First-party code never names the utility, and tokens.css keeps it from being generated
// for the vendored components/ui/ files, which cannot be edited.
import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'

const SRC = fileURLToPath(new URL('../src/', import.meta.url))
const TOKENS = fileURLToPath(new URL('../src/assets/tokens.css', import.meta.url))
const UTILITY = 'cursor-not-allowed'

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    return statSync(full).isDirectory() ? walk(full) : [full]
  })
}

const rel = (file: string): string => relative(SRC, file).replaceAll(String.fromCharCode(92), '/')

// Every class token in a file that ends in the utility, reduced to the variants in front of it
// ('' for the bare utility, 'md:disabled:' for a stacked one).
const CLASS_SEPARATOR = /[\s"'`]+/
function variantsOf(source: string): string[] {
  return source
    .split(CLASS_SEPARATOR)
    .map((token) => (token.endsWith('!') ? token.slice(0, -1) : token))
    .filter((token) => token.endsWith(UTILITY))
    .map((token) => token.slice(0, -UTILITY.length))
}
const isVendored = (file: string): boolean => rel(file).startsWith('components/ui/')
const isTest = (file: string): boolean => /\.(test|spec)\.ts$/.test(file)

describe('disabled controls use the default cursor', () => {
  it('no first-party source file names the not-allowed cursor utility', () => {
    const offenders = walk(SRC)
      .filter((file) => (file.endsWith('.vue') || file.endsWith('.ts')) && !isVendored(file) && !isTest(file))
      .filter((file) => readFileSync(file, 'utf8').includes(UTILITY))
      .map(rel)

    expect(offenders).toEqual([])
  })

  it('tokens.css keeps every variant the vendored components use from being generated', () => {
    const css = readFileSync(TOKENS, 'utf8')
    const listed = /@source not inline\("\{([^}"]*)\}cursor-not-allowed"\)/.exec(css)
    expect(listed, `tokens.css has no @source not inline(...) for ${UTILITY}`).not.toBeNull()
    const covered = new Set(listed?.[1].split(','))

    const used = walk(SRC)
      .filter(isVendored)
      .flatMap((file) => variantsOf(readFileSync(file, 'utf8')))

    expect(used.length).toBeGreaterThan(0)
    expect(used.filter((variant) => !covered.has(variant))).toEqual([])
  })

  it('reads a stacked or bracketed variant whole, so a new one is not mistaken for a listed one', () => {
    // Assembled here: Tailwind scans this file too, and a literal class would be generated.
    const variants = ['md:disabled:', 'data-[disabled]:']
    const source = ['a', ...variants.map((variant) => variant + UTILITY), `${UTILITY}!`, 'b'].join(' ')

    expect(variantsOf(source)).toEqual([...variants, ''])
  })

  it('tokens.css overrides the not-allowed cursor of a disabled toast button', () => {
    const css = readFileSync(TOKENS, 'utf8')

    expect(css).toMatch(
      /\[data-sonner-toast\]\[data-styled="true"\] \[data-disabled="true"\]\[data-disabled\]\s*\{\s*cursor:\s*default;/,
    )
  })

  it('tokens.css gives every disabled control the default cursor', () => {
    const css = readFileSync(TOKENS, 'utf8')

    expect(css).toMatch(/:is\(:disabled, \[aria-disabled="true"\], \[data-disabled\]\)\s*\{\s*cursor:\s*default;/)
  })
})
