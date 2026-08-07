// Copies the documentation site's runtime assets out of node_modules into docs/vendor/, which is
// committed so the site works offline and in a fresh clone without installing anything.
//
// Deliberately absent: docsify's bundled themes (docsify/dist/themes/**/*.css). Several of them
// (e.g. the vue theme) `@import url("https://fonts.googleapis.com/...")`, which would make the site
// depend on a remote host. docs/theme.css replaces them entirely.
import { copyFileSync, mkdirSync, rmSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))

// Exported so frontend/tests/docsVendorAssets.test.ts can assert the committed docs/vendor/ tree
// against this same list, instead of hardcoding its own copy that could drift from it.
export const NODE_MODULES = resolve(here, '..', 'node_modules')
export const VENDOR_ROOT = resolve(here, '..', '..', 'docs', 'vendor')

// Prism component order matters at load time: csharp extends clike and typescript extends
// javascript, so clike is listed first. docs/index.html loads them in this same order.
export const FILES = [
  ['docsify/dist/docsify.min.js', 'docsify.min.js'],
  ['docsify/dist/plugins/search.min.js', 'plugins/search.min.js'],
  ['prismjs/components/prism-clike.min.js', 'prism/prism-clike.min.js'],
  ['prismjs/components/prism-csharp.min.js', 'prism/prism-csharp.min.js'],
  ['prismjs/components/prism-typescript.min.js', 'prism/prism-typescript.min.js'],
  ['prismjs/components/prism-bash.min.js', 'prism/prism-bash.min.js'],
  ['prismjs/components/prism-json.min.js', 'prism/prism-json.min.js'],
]

function vendor() {
  // Wipe and rewrite, so an asset dropped from the list above does not linger in the committed tree.
  rmSync(VENDOR_ROOT, { recursive: true, force: true })
  for (const [from, to] of FILES) {
    const destination = join(VENDOR_ROOT, to)
    mkdirSync(dirname(destination), { recursive: true })
    copyFileSync(join(NODE_MODULES, from), destination)
  }
  process.stdout.write(`vendored ${FILES.length} files into docs/vendor/\n`)
}

// Only run the copy when invoked directly (`node scripts/vendor-docs-assets.mjs`, i.e. `pnpm
// docs:vendor`) -- not when imported purely for FILES/NODE_MODULES/VENDOR_ROOT, e.g. by the
// drift-guard test, which must not silently rewrite docs/vendor/ as a side effect of running.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  vendor()
}
