// Checks that need no network. For every chapter file: each marker sits above a closed fence, the
// two languages hold the same markers and byte-identical blocks, and markers and scenario captures
// match one to one (a chapter with no scenario has no captures, so any marker in it is an error),
// with each block ending in the status its scenario declares. Any other markdown file under the
// guide may hold no marker. `capture.mjs --check` runs it; the runner runs the same rules before it
// sends a request and again before it writes.
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import { CaptureError } from './errors.mjs'
import { MarkerError, blockRegion, findBlocks } from './markers.mjs'

export const LOCALES = ['zh-TW', 'en']
const CHAPTER_FILE = /^\d{2}-[^/]+\.md$/

const label = (locale, name) => `${locale}/${name}`
const byName = (a, b) => a.localeCompare(b)

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
  return [...region].reverse().find((candidate) => candidate.startsWith('HTTP_STATUS:')) ?? null
}

// Markers that exist in one language only.
function oneLanguageProblems(ids, name) {
  const allIds = [...new Set(LOCALES.flatMap((locale) => ids[locale]))]
  return allIds.flatMap((id) => {
    const present = LOCALES.filter((locale) => ids[locale].includes(id))
    return present.length === LOCALES.length ? [] : [`${id}: marker only in ${label(present[0], name)}`]
  })
}

// Blocks present in both languages must hold the same fences and the same lines.
function identityProblems(texts, ids, name) {
  const shared = ids[LOCALES[0]].filter((id) => ids[LOCALES[1]].includes(id))
  return shared
    .filter((id) => {
      const [first, second] = LOCALES.map((locale) => blockRegion(texts[locale], id))
      return first.join('\n') !== second.join('\n')
    })
    .map((id) => `${id}: block differs between ${label(LOCALES[0], name)} and ${label(LOCALES[1], name)}`)
}

function statusProblems(text, ids, declared, where) {
  return [...declared]
    .filter(([id]) => ids.includes(id))
    .flatMap(([id, expect]) => {
      const status = lastStatus(blockRegion(text, id))
      const wrong = status !== null && status !== `HTTP_STATUS:${expect}`
      return wrong ? [`${id}: ${where} ends with ${status} but the scenario declares ${expect}`] : []
    })
}

// Markers and captures match one to one in one language, and each block ends in its declared status.
function scenarioProblems(text, ids, declared, where) {
  const missing = [...declared.keys()].filter((id) => !ids.includes(id)).map((id) => `${id}: no marker in ${where}`)
  const stray = ids
    .filter((id) => !declared.has(id))
    .map((id) => `${id}: ${where} holds a marker with no capture in the scenario`)
  return [...missing, ...stray, ...statusProblems(text, ids, declared, where)]
}

// texts: { locale: markdown } for one chapter file name; declared: the capture ids the run writes
// into that file, each with the status its last exchange declares.
export function chapterProblems({ name, texts, declared = new Map(), identity }) {
  const problems = []
  const ids = Object.fromEntries(LOCALES.map((locale) => [locale, idsOf(texts[locale], label(locale, name), problems)]))
  if (LOCALES.some((locale) => ids[locale] === null)) return problems

  problems.push(...oneLanguageProblems(ids, name))
  if (identity) problems.push(...identityProblems(texts, ids, name))
  for (const locale of LOCALES) {
    problems.push(...scenarioProblems(texts[locale], ids[locale], declared, label(locale, name)))
  }
  return problems
}

export function readTexts(guideDir, name) {
  return Object.fromEntries(LOCALES.map((locale) => [locale, readFileSync(join(guideDir, locale, name), 'utf8')]))
}

function chapterNames(guideDir) {
  const names = LOCALES.filter((locale) => existsSync(join(guideDir, locale))).flatMap((locale) =>
    readdirSync(join(guideDir, locale)).filter((entry) => CHAPTER_FILE.test(entry)),
  )
  return [...new Set(names)].sort(byName)
}

function markdownLabels(guideDir) {
  return readdirSync(guideDir, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith('.md'))
    .map((entry) => relative(guideDir, join(entry.parentPath, entry.name)).split(sep).join('/'))
    .filter((path) => !path.split('/').some((part) => part.startsWith('.')))
    .sort(byName)
}

function isChapterFile(path) {
  const parts = path.split('/')
  return parts.length === 2 && LOCALES.includes(parts[0]) && CHAPTER_FILE.test(parts[1])
}

// Markdown outside the chapter files (an index, a changelog, anything nested) may not hold a marker.
function otherFileProblems(guideDir) {
  return markdownLabels(guideDir)
    .filter((path) => !isChapterFile(path))
    .flatMap((path) => {
      try {
        const found = findBlocks(readFileSync(join(guideDir, path), 'utf8'), { file: path })
        return found.length > 0 ? [`${path}: markers are allowed only in chapter files (NN-name.md under a locale)`] : []
      } catch (error) {
        if (!(error instanceof MarkerError)) throw error
        return [error.message]
      }
    })
}

function missingLanguageProblem(name, present) {
  const absent = LOCALES.filter((locale) => !present.includes(locale))
  if (present.length === 0) return `${name}: has captures in the scenario but no file in ${absent.join(' or ')}`
  return `${name}: exists only in ${present.join(', ')}; missing in ${absent.join(', ')}`
}

function markerCount(text) {
  try {
    return findBlocks(text).length
  } catch (error) {
    if (error instanceof MarkerError) return 0
    throw error
  }
}

export function checkGuide({ guideDir, groups }) {
  const declared = declaredByFile(groups)
  const problems = otherFileProblems(guideDir)
  let blocks = 0
  let files = 0
  const names = [...new Set([...chapterNames(guideDir), ...declared.keys()])].sort(byName)
  for (const name of names) {
    const present = LOCALES.filter((locale) => existsSync(join(guideDir, locale, name)))
    files += present.length
    if (present.length < LOCALES.length) {
      problems.push(missingLanguageProblem(name, present))
      continue
    }
    const texts = readTexts(guideDir, name)
    problems.push(...chapterProblems({ name, texts, declared: declared.get(name), identity: true }))
    blocks += markerCount(texts[LOCALES[0]])
  }
  return { problems, blocks, files }
}
