// Fails the docs build when a manual table cannot fit the site's content
// column. The measuring rules and thresholds live in lib/markdown-tables.mjs;
// this file only decides which files are scanned and how failures are shown.
//
// Runs after vitepress build in `pnpm build` (docs/package.json), next to
// check-rendered-chapters.mjs, so the same command that gates links and
// rendered output also gates table width. It reads sources, not dist, so it
// needs no build and can be run on its own while editing a chapter.
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { LIMITS, checkTables } from './lib/markdown-tables.mjs'

const DEFAULT_GUIDE = join(dirname(fileURLToPath(import.meta.url)), '..', 'guide')
const guideRoot = process.argv[2] ?? DEFAULT_GUIDE

// The manual is being rewritten in batches (2026-09-10 rewrite spec). The
// sixteen chapters below predate the width rule and are replaced batch by
// batch; until batch 5 deletes them they would fail this check in every
// locale. This is the ONLY exemption, it is by filename so it applies to both
// locales at once, and it is removed together with the files. Do not add to it:
// a new chapter that does not fit gets rewritten, not listed here.
const LEGACY_CHAPTERS = new Set([
  '01-introduction-and-architecture.md',
  '02-getting-started.md',
  '03-configuration-reference.md',
  '04-defining-a-collection.md',
  '05-field-types.md',
  '06-internationalization.md',
  '07-relations.md',
  '08-query-dsl.md',
  '09-rest-api.md',
  '10-graphql-api.md',
  '11-files-and-media.md',
  '12-auth-and-rbac.md',
  '13-revisions-and-soft-delete.md',
  '14-admin-spa-customization.md',
  '15-deployment-operations-testing.md',
  '16-sample-walkthrough.md',
])

if (!existsSync(guideRoot) || !statSync(guideRoot).isDirectory()) {
  process.stderr.write(`${guideRoot}: not a directory\n`)
  process.exit(1)
}

// Every markdown file under guide/, at any depth, forward-slash relative
// paths so messages read the same on every platform. Dot-prefixed path
// segments are skipped, the same way the sidebar and the rendered-chapters
// guard skip them.
const markdownFiles = readdirSync(guideRoot, { recursive: true, withFileTypes: true })
  .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith('.md'))
  .map((entry) => relative(guideRoot, join(entry.parentPath, entry.name)).split(sep).join('/'))
  .filter((path) => !path.split('/').some((segment) => segment.startsWith('.')))
  .sort()

const failures = []
let checked = 0
let skipped = 0

for (const path of markdownFiles) {
  const fileName = path.split('/').at(-1)
  if (LEGACY_CHAPTERS.has(fileName)) {
    skipped += 1
    continue
  }
  checked += 1
  const markdown = readFileSync(join(guideRoot, path), 'utf8')
  for (const violation of checkTables(markdown, LIMITS)) {
    failures.push(`${path}:${violation.line}: ${violation.detail}`)
  }
}

if (failures.length > 0) {
  process.stderr.write(`${failures.join('\n')}\n`)
  process.stderr.write(
    `\n${failures.length} table violation(s). Tables may have at most ${LIMITS.maxColumns} columns and ` +
      `cells of display width ${LIMITS.maxCellWidth} (CJK counts 2). Rewrite as a list or a section — ` +
      'see docs/ai/conventions.md, "How documentation is written".\n',
  )
  process.exit(1)
}

process.stdout.write(
  `checked ${checked} markdown files for table width` +
    (skipped > 0 ? `, ${skipped} legacy chapter files skipped` : '') +
    '\n',
)
