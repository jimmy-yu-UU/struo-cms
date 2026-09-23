// vitepress build exits 0 even when a page fails to render, but not every {{ }}
// fails the same way. A literal {{ a.b }} in prose or an inline code span makes
// Vue's compiler throw, and the page is emitted with page chrome, correct
// filename, and an empty body. A bare identifier — {{ ident }} — throws
// nothing: it interpolates to the empty string and empties only its own
// sentence, not the whole page. Either way the chapter (or sentence) vanishes
// from the search index, which is built from rendered content, and page counts
// stay right, so nothing about the file listing shows it.
//
// This guard asserts the outcome instead of the cause: one rendered page per
// chapter source, and a heading inside each rendered page. It catches the
// {{ a.b }} form, whose empty body fails the heading check. It does not, on
// its own, catch the {{ ident }} form — the page still renders a heading, just
// with a hole in one sentence. The source-side scan below closes that gap: it
// walks the chapter sources themselves for a bare {{ outside fenced code and
// unwrapped by <span v-pre>, which catches both forms before any build runs.
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { fencedLines } from './lib/fences.mjs'

const DOCS_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..')
const DIST = join(DOCS_ROOT, '.vitepress', 'dist')
const GUIDE = join(DOCS_ROOT, 'guide')
// [^/]+ rather than .+: the pattern is applied to a relative path with forward
// slashes, and a dot matches a slash, so .+ here would also match a chapter
// nested under a subdirectory (e.g. "01-dir/readme.md") as if it were a
// top-level chapter file. [^/] keeps a match to exactly one path segment.
const CHAPTER_SOURCE = /^\d{2}-[^/]+\.md$/
// Files directly under a locale that are allowed not to be chapters. A file
// nested in a subdirectory is never exempt by this — it is reported as a
// stray filename same as a bad top-level name, since it is equally invisible
// to the sidebar. changelog.md is the per-locale version history, linked
// from the nav rather than the sidebar.
const NON_CHAPTER = new Set(['index.md', 'changelog.md'])

const REMEDY =
  'A literal {{ }} in prose or an inline code span is the usual cause; wrap it in <span v-pre>. ' +
  'vitepress build exits 0 in this state, which is why this check exists.'

const MUSTACHE_LOCATION =
  'see docs/ai/conventions.md, "Mustache syntax in the manual"'

// Catches {{ ident }} as well as {{ a.b }} — the bare-identifier form throws
// nothing, renders a heading, and is invisible to the rendered-output checks
// below. Runs on the sources directly, so it needs no build.
//
// Fence detection (see lib/fences.mjs) keeps the manual's many
// {{ }}-containing code samples out of this scan — a fenced line is never
// prose, opener and closer included.
function findBareMustaches(file) {
  const lines = readFileSync(file, 'utf8').split(/\r?\n/)
  const fenced = fencedLines(lines)
  const lineNumbers = []
  lines.forEach((line, index) => {
    if (!fenced[index] && line.includes('{{') && !line.includes('v-pre')) {
      lineNumbers.push(index + 1)
    }
  })
  return lineNumbers
}

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
let checkedRoot = 0
let checkedChapters = 0

// Files directly under guide/ — the bilingual landing page and the changelog
// files — sit outside every locale directory, so the per-locale loop below
// never sees them. Same two assertions as a chapter, applied to each, plus
// the source-side mustache scan.
const rootSources = readdirSync(GUIDE, { withFileTypes: true })
  .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith('.md'))
  .map((entry) => entry.name)
  .sort()
if (!rootSources.includes('index.md')) {
  failures.push('index.md: missing directly under guide/ — the bilingual landing page must exist')
}
for (const name of rootSources) {
  const page = join(DIST, name.replace(/\.md$/, '.html'))
  if (!existsSync(page)) {
    failures.push(`${name}: no rendered page at ${page}`)
  } else {
    checkedRoot += 1
    if (!/<h1\b/.test(readFileSync(page, 'utf8'))) {
      failures.push(`${name}: rendered page has no <h1> — its body is empty. ${REMEDY}`)
    }
  }
  for (const lineNumber of findBareMustaches(join(GUIDE, name))) {
    failures.push(
      `${name}:${lineNumber}: contains "{{" outside a fenced code block and not wrapped in ` +
        `<span v-pre> — ${MUSTACHE_LOCATION}`,
    )
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

    for (const lineNumber of findBareMustaches(join(localeDir, name))) {
      failures.push(
        `${locale}/${name}:${lineNumber}: contains "{{" outside a fenced code block and not wrapped in ` +
          `<span v-pre> — ${MUSTACHE_LOCATION}`,
      )
    }
  }

  const rendered = [...chapters, ...markdown.filter((name) => NON_CHAPTER.has(name))]
  for (const chapter of rendered) {
    const page = join(DIST, locale, chapter.replace(/\.md$/, '.html'))

    if (!existsSync(page)) {
      failures.push(`${locale}/${chapter}: no rendered page at ${page}`)
      continue
    }

    checkedChapters += 1
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

process.stdout.write(
  `checked ${checkedRoot} rendered root page and ${checkedChapters} rendered chapter pages\n`,
)
