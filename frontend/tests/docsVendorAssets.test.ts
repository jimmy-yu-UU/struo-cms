// @vitest-environment node
// This test reads files from disk and needs no DOM. Under the jsdom environment Vitest transforms
// the file in Vite's client mode, where `new URL(<literal>, import.meta.url)` is rewritten and
// fileURLToPath then rejects the result -- the same reason docsSidebars.test.ts pins node.
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, it, expect } from 'vitest'
import { FILES, NODE_MODULES, VENDOR_ROOT } from '../scripts/vendor-docs-assets.mjs'

// Re-derived only for a sanity check that the imported VENDOR_ROOT points where this test expects
// -- everything else in this file uses VENDOR_ROOT/NODE_MODULES/FILES straight from the vendoring
// script, so the expected path set has exactly one source of truth.
const EXPECTED_VENDOR_ROOT = fileURLToPath(new URL('../../docs/vendor/', import.meta.url))

const REVENDOR_HINT =
  're-run `pnpm -C frontend docs:vendor` and re-commit docs/vendor/ (this keeps the committed ' +
  'runtime and THIRD-PARTY-NOTICES.md version strings in sync with frontend/package.json and ' +
  'frontend/pnpm-lock.yaml)'

function listFilesRecursive(root: string): string[] {
  const out: string[] = []
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const full = join(root, entry.name)
    if (entry.isDirectory()) out.push(...listFilesRecursive(full))
    else out.push(full)
  }
  return out
}

describe('docs/vendor/ drift guard', () => {
  it('VENDOR_ROOT (from vendor-docs-assets.mjs) resolves to docs/vendor/', () => {
    // Compare directory identity via a real file both paths can see, not string equality --
    // VENDOR_ROOT is OS-native (backslashes on Windows), EXPECTED_VENDOR_ROOT comes from a file
    // URL (forward slashes); normalising both through statSync avoids a false mismatch.
    expect(statSync(VENDOR_ROOT).isDirectory()).toBe(true)
    expect(statSync(EXPECTED_VENDOR_ROOT).isDirectory()).toBe(true)
    expect(relative(VENDOR_ROOT, EXPECTED_VENDOR_ROOT)).toBe('')
  })

  it('docs/vendor/ contains exactly the files vendor-docs-assets.mjs lists, nothing else', () => {
    const expected = new Set(FILES.map(([, to]) => to.replace(/\\/g, '/')))
    const actual = new Set(
      listFilesRecursive(VENDOR_ROOT).map((f) => relative(VENDOR_ROOT, f).replace(/\\/g, '/')),
    )

    const missing = [...expected].filter((f) => !actual.has(f))
    const extra = [...actual].filter((f) => !expected.has(f))

    expect(missing, `missing from docs/vendor/ -- ${REVENDOR_HINT}`).toEqual([])
    expect(extra, `unexpected files in docs/vendor/ (not in FILES) -- ${REVENDOR_HINT}`).toEqual([])
  })

  for (const [from, to] of FILES) {
    it(`docs/vendor/${to.replace(/\\/g, '/')} is byte-identical to node_modules/${from}`, () => {
      const vendored = readFileSync(join(VENDOR_ROOT, to))
      const source = readFileSync(join(NODE_MODULES, from))
      const identical = vendored.equals(source)
      expect(
        identical,
        `docs/vendor/${to} has drifted from frontend/node_modules/${from} -- ${REVENDOR_HINT}`,
      ).toBe(true)
    })
  }
})
