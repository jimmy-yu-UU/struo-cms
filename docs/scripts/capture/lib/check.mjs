// Checks that need no network: every marker sits above a closed fence, the two languages hold the
// same markers and byte-identical blocks, and (for chapters with a scenario) markers and captures
// match one to one and each block ends with the status its scenario declares. `capture.mjs --check`
// runs it; the runner runs the same rules before it sends a request and again before it writes.
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { CaptureError } from './errors.mjs'
import { MarkerError, blockRegion, findBlocks } from './markers.mjs'

export const LOCALES = ['zh-TW', 'en']
const CHAPTER_FILE = /^\d{2}-[^/]+\.md$/

const label = (locale, name) => `${locale}/${name}`

// The capture ids of a run, grouped by the chapter file their blocks live in. A capture step names
// its file itself or takes the file of its group; the groups may feed one file from several places
// in the run. Each id keeps the status its last exchange declares.
export function declaredByFile(groups) {
  const byFile = new Map()
  const owner = new Map()
  for (const group of groups) {
    for (const step of group.steps.filter((candidate) => candidate.capture)) {
      const file = step.file ?? group.file
      if (!file) throw new CaptureError(`${step.capture}: names no chapter file; neither the step nor its group ${group.name} has one`)
      if (owner.has(step.capture) && owner.get(step.capture) !== file) {
        throw new CaptureError(`${step.capture}: captured into both ${owner.get(step.capture)} and ${file}`)
      }
      owner.set(step.capture, file)
      if (!byFile.has(file)) byFile.set(file, new Map())
      byFile.get(file).set(step.capture, step.expect)
    }
  }
  return byFile
}

function idsOf(text, file, problems) {
  try {
    return findBlocks(text, { file }).map((block) => block.id)
  } catch (error) {
    if (!(error instanceof MarkerError)) throw error
    problems.push(error.message)
    return null
  }
}

function lastStatus(region) {
  const line = [...region].reverse().find((candidate) => candidate.startsWith('HTTP_STATUS:'))
  return line ?? null
}

// texts: { locale: markdown } for one chapter name; chapter: its scenario entry or undefined.
export function chapterProblems({ name, texts, declared = new Map(), identity }) {
  const problems = []
  const ids = {}
  for (const locale of LOCALES) {
    ids[locale] = idsOf(texts[locale], label(locale, name), problems)
  }
  if (LOCALES.some((locale) => ids[locale] === null)) return problems

  const allIds = [...new Set(LOCALES.flatMap((locale) => ids[locale]))]
  for (const id of allIds) {
    const missing = LOCALES.filter((locale) => !ids[locale].includes(id))
    if (missing.length > 0 && missing.length < LOCALES.length) {
      const present = LOCALES.find((locale) => !missing.includes(locale))
      problems.push(`${id}: marker only in ${label(present, name)}`)
    }
  }

  if (identity) {
    for (const id of allIds.filter((candidate) => LOCALES.every((locale) => ids[locale].includes(candidate)))) {
      const [zh, en] = LOCALES.map((locale) => blockRegion(texts[locale], id))
      if (zh.join('\n') !== en.join('\n')) {
        problems.push(`${id}: block differs between ${label(LOCALES[0], name)} and ${label(LOCALES[1], name)}`)
      }
    }
  }

  for (const locale of LOCALES) {
    for (const id of declared.keys()) {
      if (!ids[locale].includes(id)) problems.push(`${id}: no marker in ${label(locale, name)}`)
    }
    for (const id of ids[locale]) {
      if (!declared.has(id)) problems.push(`${id}: ${label(locale, name)} holds a marker with no capture in the scenario`)
    }
    for (const [id, expect] of declared) {
      if (!ids[locale].includes(id)) continue
      const status = lastStatus(blockRegion(texts[locale], id))
      if (status !== null && status !== `HTTP_STATUS:${expect}`) {
        problems.push(`${id}: ${label(locale, name)} ends with ${status} but the scenario declares ${expect}`)
      }
    }
  }
  return problems
}

export function readTexts(guideDir, name) {
  return Object.fromEntries(LOCALES.map((locale) => [locale, readFileSync(join(guideDir, locale, name), 'utf8')]))
}

function chapterNames(guideDir) {
  const names = new Set()
  for (const locale of LOCALES) {
    const dir = join(guideDir, locale)
    if (!existsSync(dir)) continue
    for (const entry of readdirSync(dir)) if (CHAPTER_FILE.test(entry)) names.add(entry)
  }
  return [...names].sort()
}

export function checkGuide({ guideDir, groups }) {
  const problems = []
  const declared = declaredByFile(groups)
  let blocks = 0
  let files = 0
  const names = new Set([...chapterNames(guideDir), ...declared.keys()])
  for (const name of [...names].sort()) {
    const present = LOCALES.filter((locale) => existsSync(join(guideDir, locale, name)))
    files += present.length
    if (present.length < LOCALES.length) {
      const absent = LOCALES.filter((locale) => !present.includes(locale))
      problems.push(
        present.length === 0
          ? `${name}: has captures in the scenario but no file in ${absent.join(" or ")}`
          : `${name}: exists only in ${present.join(", ")}; missing in ${absent.join(", ")}`,
      )
      continue
    }
    const texts = readTexts(guideDir, name)
    problems.push(...chapterProblems({ name, texts, declared: declared.get(name), identity: true }))
    try {
      blocks += findBlocks(texts[LOCALES[0]]).length
    } catch (error) {
      if (!(error instanceof MarkerError)) throw error
    }
  }
  return { problems, blocks, files }
}
