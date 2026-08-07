// Copies the documentation site's runtime assets out of node_modules into docs/vendor/, which is
// committed so the site works offline and in a fresh clone without installing anything.
//
// Deliberately absent: docsify's bundled themes (docsify/dist/themes/**/*.css). Several of them
// (e.g. the vue theme) `@import url("https://fonts.googleapis.com/...")`, which would make the site
// depend on a remote host. docs/theme.css replaces them entirely.
import { copyFileSync, mkdirSync, rmSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const nodeModules = resolve(here, '..', 'node_modules')
const vendorRoot = resolve(here, '..', '..', 'docs', 'vendor')

// Prism component order matters at load time: csharp extends clike and typescript extends
// javascript, so clike is listed first. docs/index.html loads them in this same order.
const FILES = [
  ['docsify/dist/docsify.min.js', 'docsify.min.js'],
  ['docsify/dist/plugins/search.min.js', 'plugins/search.min.js'],
  ['prismjs/components/prism-clike.min.js', 'prism/prism-clike.min.js'],
  ['prismjs/components/prism-csharp.min.js', 'prism/prism-csharp.min.js'],
  ['prismjs/components/prism-typescript.min.js', 'prism/prism-typescript.min.js'],
  ['prismjs/components/prism-bash.min.js', 'prism/prism-bash.min.js'],
  ['prismjs/components/prism-json.min.js', 'prism/prism-json.min.js'],
  ['prismjs/components/prism-yaml.min.js', 'prism/prism-yaml.min.js'],
  ['prismjs/components/prism-sql.min.js', 'prism/prism-sql.min.js'],
]

// Wipe and rewrite, so an asset dropped from the list above does not linger in the committed tree.
rmSync(vendorRoot, { recursive: true, force: true })
for (const [from, to] of FILES) {
  const destination = join(vendorRoot, to)
  mkdirSync(dirname(destination), { recursive: true })
  copyFileSync(join(nodeModules, from), destination)
}
process.stdout.write(`vendored ${FILES.length} files into docs/vendor/\n`)
