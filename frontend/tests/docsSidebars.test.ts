// @vitest-environment node
// This test reads files from disk and needs no DOM. Under the jsdom environment Vitest transforms
// the file in Vite's client mode, where `new URL(<literal>, import.meta.url)` is rewritten and
// fileURLToPath then rejects the result — the same reason schemaContract.test.ts pins node.
import { existsSync, readFileSync } from 'node:fs'
import { dirname, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, it, expect } from 'vitest'

const DOCS_ROOT = fileURLToPath(new URL('../../docs/', import.meta.url))

// Relative to this test file. Each entry is a docsify sidebar; docsify resolves the links inside
// them against the sidebar's own directory, which is what `resolve(dirname(sidebar), href)` mirrors.
const SIDEBARS = [
  '../../docs/_sidebar.md',
  '../../docs/guide/en/_sidebar.md',
  '../../docs/guide/zh-TW/_sidebar.md',
] as const

const MARKDOWN_LINK = /\[[^\]]*\]\(([^)]+)\)/g

function localTargets(sidebarPath: string): string[] {
  const dir = dirname(sidebarPath)
  const targets: string[] = []
  for (const match of readFileSync(sidebarPath, 'utf8').matchAll(MARKDOWN_LINK)) {
    const href = match[1].trim()
    // Skip anything docsify does not resolve to a file on disk: absolute URLs, protocol-relative
    // URLs, and links that are only a fragment.
    if (/^([a-z][a-z0-9+.-]*:|\/\/|#)/i.test(href)) continue
    targets.push(resolve(dir, href.split('#')[0]))
  }
  return targets
}

describe('docs site sidebars', () => {
  for (const sidebarRelative of SIDEBARS) {
    const sidebarPath = fileURLToPath(new URL(sidebarRelative, import.meta.url))

    it(`${sidebarRelative} exists`, () => {
      expect(existsSync(sidebarPath)).toBe(true)
    })

    it(`${sidebarRelative} links only to files that exist`, () => {
      const targets = localTargets(sidebarPath)
      expect(targets.length).toBeGreaterThan(0)
      const missing = targets
        .filter((target) => !existsSync(target))
        .map((target) => relative(DOCS_ROOT, target).replace(/\\/g, '/'))
      expect(missing).toEqual([])
    })
  }
})
