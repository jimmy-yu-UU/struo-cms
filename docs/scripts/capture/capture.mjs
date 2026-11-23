// Re-captures the manual's HTTP transcripts from a running host and writes the same text into both
// languages. See docs/ai/task-playbooks.md, "Re-capture the manual's transcripts".
//
//   STRUO_CAPTURE_ADMIN_PASSWORD=... node scripts/capture/capture.mjs \
//     --base-url http://localhost:5221 --chapters 10,11 --write
//
//   --check        no network: markers, cross-language identity, scenario/marker match
//   --write        write the chapter files (without it the captured blocks are only printed)
//   --chapters     comma-separated chapter numbers (default: every chapter with a scenario)
//   --base-url     or STRUO_CAPTURE_BASE_URL
//   --email        or STRUO_CAPTURE_ADMIN_EMAIL (default admin@admin.com)
//   --password     or STRUO_CAPTURE_ADMIN_PASSWORD (the variable keeps it out of the process list)
//   --guide-dir    the manual's root (default: ../../guide); must lie inside the working directory
import { realpathSync, statSync } from 'node:fs'
import { dirname, join, sep } from 'node:path'
import { parseArgs } from 'node:util'
import { fileURLToPath } from 'node:url'
import { checkGuide } from './lib/check.mjs'
import { CaptureError, runCapture } from './lib/runner.mjs'
import { CHAPTERS, SEED } from './scenarios/index.mjs'

const DEFAULT_GUIDE = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'guide')

// A directory taken from the command line is resolved, confined to the working directory and only
// then touched, as docs/scripts/check-table-width.mjs does.
function confinedDirectory(candidate) {
  let resolved
  try {
    resolved = realpathSync(candidate)
  } catch {
    throw new CaptureError(`${candidate}: not a directory`)
  }
  const base = realpathSync(process.cwd())
  if (resolved !== base && !resolved.startsWith(base + sep)) {
    throw new CaptureError(`${candidate}: outside the working directory ${base}`)
  }
  if (!statSync(resolved).isDirectory()) throw new CaptureError(`${candidate}: not a directory`)
  return resolved
}

function selectChapters(option) {
  if (!option) return CHAPTERS
  return option.split(',').map((number) => {
    const chapter = CHAPTERS.find((candidate) => candidate.number === number.trim())
    if (!chapter) throw new CaptureError(`no scenario for chapter ${number.trim()}`)
    return chapter
  })
}

async function main() {
  const { values } = parseArgs({
    options: {
      check: { type: 'boolean', default: false },
      write: { type: 'boolean', default: false },
      chapters: { type: 'string' },
      'base-url': { type: 'string' },
      email: { type: 'string' },
      password: { type: 'string' },
      'guide-dir': { type: 'string' },
    },
  })
  const guideOption = values['guide-dir'] ?? DEFAULT_GUIDE

  if (values.check) {
    const guideDir = confinedDirectory(guideOption)
    const { problems, blocks, files } = checkGuide({ guideDir, chapters: CHAPTERS })
    if (problems.length > 0) throw new CaptureError(`${problems.join('\n')}\n${problems.length} problem(s) in the capture markers`)
    process.stdout.write(`checked ${blocks} marked blocks in ${files} files\n`)
    return
  }

  const baseUrl = values['base-url'] ?? process.env.STRUO_CAPTURE_BASE_URL
  if (!baseUrl) throw new CaptureError('no base URL: pass --base-url or set STRUO_CAPTURE_BASE_URL')
  const adminPassword = values.password ?? process.env.STRUO_CAPTURE_ADMIN_PASSWORD
  if (!adminPassword) throw new CaptureError('no admin password: set STRUO_CAPTURE_ADMIN_PASSWORD')
  const adminEmail = values.email ?? process.env.STRUO_CAPTURE_ADMIN_EMAIL ?? 'admin@admin.com'

  const guideDir = confinedDirectory(guideOption)
  const chapters = selectChapters(values.chapters)
  const { blocks, files } = await runCapture({
    baseUrl,
    vars: { adminEmail, adminPassword },
    seed: SEED,
    chapters,
    guideDir,
    write: values.write,
  })
  for (const [id, text] of blocks) process.stdout.write(`--- ${id}\n${text}\n`)
  const changed = files.filter((file) => file.changed).length
  process.stdout.write(`${values.write ? 'wrote' : 'would write'} ${blocks.size} blocks into ${changed} files\n`)
}

main().catch((error) => {
  if (!(error instanceof CaptureError)) {
    process.stderr.write(`${error.stack ?? error}\n`)
  } else {
    process.stderr.write(`capture: ${error.message}\n`)
  }
  process.exit(1)
})
