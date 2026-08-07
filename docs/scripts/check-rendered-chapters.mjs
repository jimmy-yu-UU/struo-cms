// vitepress build exits 0 even when a page fails to render: a literal {{ }} in
// prose or an inline code span makes Vue's compiler throw, and the page is
// emitted with page chrome, correct filename, and an empty body. The chapter
// then also vanishes from the search index, which is built from rendered
// content. Page counts stay right, so nothing about the file listing shows it.
//
// This guard asserts the outcome instead of the cause: one rendered page per
// chapter source, and a heading inside each rendered page. It does not attempt
// to detect a partially rendered page — the observed failure loses the whole
// body.
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const DOCS_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..')
const DIST = join(DOCS_ROOT, '.vitepress', 'dist')
const GUIDE = join(DOCS_ROOT, 'guide')
const CHAPTER_SOURCE = /^\d{2}-.+\.md$/
// Files directly under a locale that are allowed not to be chapters. A file
// nested in a subdirectory is never exempt by this — it is reported as a
// stray filename same as a bad top-level name, since it is equally invisible
// to the sidebar.
const NON_CHAPTER = new Set(['index.md'])

const REMEDY =
  'A literal {{ }} in prose or an inline code span is the usual cause; wrap it in <span v-pre>. ' +
  'vitepress build exits 0 in this state, which is why this check exists.'

// Each locale is one subdirectory of guide/ — that is exactly what srcDir's
// locale-prefix convention (docs/.vitepress/config.mts) makes a locale.
// Deriving the list from the filesystem instead of writing it down means a
// locale added to config.mts's `locales` map but not to this list cannot go
// unchecked: it would simply not exist as a directory to derive.
const LOCALES = readdirSync(GUIDE, { withFileTypes: true })
  .filter((entry) => entry.isDirectory() && !entry.name.startsWith('.'))
  .map((entry) => entry.name)
  .sort()

if (LOCALES.length === 0) {
  process.stderr.write('guide/: no locale directories found\n')
  process.exit(1)
}

const failures = []
const chaptersByLocale = new Map()
let checked = 0

// guide/index.md is the bilingual root landing page and sits outside every
// locale directory, so the per-locale loop below never sees it. Same two
// assertions as a chapter, applied once: it rendered, and it is not empty.
const rootPage = join(DIST, 'index.html')
if (!existsSync(rootPage)) {
  failures.push(`index.md: no rendered page at ${rootPage}`)
} else {
  checked += 1
  if (!/<h1\b/.test(readFileSync(rootPage, 'utf8'))) {
    failures.push(`index.md: rendered page has no <h1> — its body is empty. ${REMEDY}`)
  }
}

for (const locale of LOCALES) {
  const localeDir = join(GUIDE, locale)
  // Recursive so a file nested in a subdirectory is not invisible to this
  // check the way it is to the sidebar deriver's flat listing — it must still
  // surface, just as a stray filename rather than a chapter. Paths are
  // normalised to forward slashes so the failure message reads the same on
  // every platform regardless of readdirSync's native separator.
  const markdown = readdirSync(localeDir, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => relative(localeDir, join(entry.parentPath, entry.name)).split(sep).join('/'))
    .filter((name) => name.toLowerCase().endsWith('.md'))
  const chapters = markdown.filter((name) => CHAPTER_SOURCE.test(name)).sort()
  chaptersByLocale.set(locale, new Set(chapters))

  if (chapters.length === 0) {
    failures.push(`${locale}: no chapter sources found under guide/${locale}`)
    continue
  }

  // A file the sidebar deriver's pattern rejects is not in navigation at all,
  // and nothing else would ever say so — `9-x.md`, `17_x.md`, `17-X.MD` and
  // anything nested in a subdirectory are all invisible to it. Naming it here
  // is the positive completeness property the deriver itself cannot provide.
  for (const name of markdown) {
    if (!CHAPTER_SOURCE.test(name) && !NON_CHAPTER.has(name)) {
      failures.push(
        `${locale}/${name}: not a chapter filename (NN-name.md) and not ${[...NON_CHAPTER].join(' or ')}, ` +
          'so it is absent from the sidebar. Rename it or add it to NON_CHAPTER.',
      )
    }
  }

  for (const chapter of chapters) {
    const page = join(DIST, locale, chapter.replace(/\.md$/, '.html'))

    if (!existsSync(page)) {
      failures.push(`${locale}/${chapter}: no rendered page at ${page}`)
      continue
    }

    checked += 1
    if (!/<h1\b/.test(readFileSync(page, 'utf8'))) {
      failures.push(`${locale}/${chapter}: rendered page has no <h1> — its body is empty. ${REMEDY}`)
    }
  }
}

// The locales are derived independently by the sidebar, so a chapter added to
// one and forgotten in the others produces silently unequal sidebars.
const [first, ...rest] = LOCALES
for (const other of rest) {
  const missing = [...chaptersByLocale.get(first)].filter((name) => !chaptersByLocale.get(other).has(name))
  const extra = [...chaptersByLocale.get(other)].filter((name) => !chaptersByLocale.get(first).has(name))
  for (const name of missing) {
    failures.push(`${other}/${name}: present in ${first} but missing here — the two sidebars disagree`)
  }
  for (const name of extra) {
    failures.push(`${first}/${name}: present in ${other} but missing here — the two sidebars disagree`)
  }
}

if (failures.length > 0) {
  process.stderr.write(`${failures.join('\n')}\n`)
  process.exit(1)
}

process.stdout.write(`checked ${checked} rendered chapter pages\n`)
