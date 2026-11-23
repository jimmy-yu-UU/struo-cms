// Re-captures the manual's HTTP transcripts from a running host and writes the same text into both
// languages. See docs/ai/task-playbooks.md, "Re-capture the manual's transcripts".
//
//   STRUO_CAPTURE_ADMIN_PASSWORD=... node scripts/capture/capture.mjs \
//     --base-url http://localhost:5221 --chapters 10,11 --write
//
//   --check        no network: markers, cross-language identity, scenario/marker match
//   --write        write the chapter files (without it the captured blocks are only printed)
//   --chapters     comma-separated chapter numbers whose files are written; the run itself always
//                  executes every group in order (default: every file with a capture)
//   --base-url     or STRUO_CAPTURE_BASE_URL
//   --email        or STRUO_CAPTURE_ADMIN_EMAIL (default admin@admin.com)
//   --guide-dir    the manual's root (default: ../../guide); must lie inside the working directory
//
// The admin password comes from STRUO_CAPTURE_ADMIN_PASSWORD only.
import { realpathSync, statSync } from 'node:fs'
import { dirname, join, sep } from 'node:path'
import { parseArgs } from 'node:util'
import { fileURLToPath } from 'node:url'
import { checkGuide } from './lib/check.mjs'
import { CaptureError } from './lib/errors.mjs'
import { runCapture } from './lib/runner.mjs'
import { selectFiles, summarize } from './lib/select.mjs'
import { CONTENT_COLLECTIONS, GROUPS } from './scenarios/index.mjs'

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

async function main() {
  const { values } = parseArgs({
    options: {
      check: { type: 'boolean', default: false },
      write: { type: 'boolean', default: false },
      chapters: { type: 'string' },
      'base-url': { type: 'string' },
      email: { type: 'string' },
      'guide-dir': { type: 'string' },
    },
  })
  const guideOption = values['guide-dir'] ?? DEFAULT_GUIDE

  if (values.check) {
    const guideDir = confinedDirectory(guideOption)
    const { problems, blocks, files } = checkGuide({ guideDir, groups: GROUPS })
    if (problems.length > 0) throw new CaptureError(`${problems.join('\n')}\n${problems.length} problem(s) in the capture markers`)
    process.stdout.write(`checked ${blocks} marked blocks in ${files} files\n`)
    return
  }

  const baseUrl = values['base-url'] ?? process.env.STRUO_CAPTURE_BASE_URL
  if (!baseUrl) throw new CaptureError('no base URL: pass --base-url or set STRUO_CAPTURE_BASE_URL')
  const adminPassword = process.env.STRUO_CAPTURE_ADMIN_PASSWORD
  if (!adminPassword) throw new CaptureError('no admin password: set STRUO_CAPTURE_ADMIN_PASSWORD')
  const adminEmail = values.email ?? process.env.STRUO_CAPTURE_ADMIN_EMAIL ?? 'admin@admin.com'

  const guideDir = confinedDirectory(guideOption)
  const only = selectFiles(values.chapters, GROUPS)
  const { blocks, files } = await runCapture({
    baseUrl,
    vars: { adminEmail, adminPassword },
    groups: GROUPS,
    only,
    freshCollections: CONTENT_COLLECTIONS,
    guideDir,
    write: values.write,
  })
  for (const [id, text] of blocks) process.stdout.write(`--- ${id}\n${text}\n`)
  process.stdout.write(`${summarize({ write: values.write, files, only })}\n`)
}

try {
  await main()
} catch (error) {
  const report = error instanceof CaptureError ? `capture: ${error.message}` : (error.stack ?? String(error))
  process.stderr.write(`${report}\n`)
  process.exitCode = 1
}
