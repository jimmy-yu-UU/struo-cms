// Checks that need no network: every marker sits above a closed fence, the two languages hold the
// same markers and byte-identical blocks, and (for chapters with a scenario) markers and captures
// match one to one and each block ends with the status its scenario declares. `capture.mjs --check`
// runs it; the runner runs the same rules before it sends a request and again before it writes.
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { MarkerError, blockRegion, findBlocks } from './markers.mjs'

export const LOCALES = ['zh-TW', 'en']
const CHAPTER_FILE = /^\d{2}-[^/]+\.md$/

const label = (locale, name) => `${locale}/${name}`

// Capture ids of a scenario in order, each with the status its last exchange declares.
export function declaredCaptures(steps) {
  const declared = new Map()
  for (const step of steps.filter((candidate) => candidate.capture)) declared.set(step.capture, step.expect)
  return declared
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
export function chapterProblems({ name, texts, chapter, identity }) {
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

  if (chapter) {
    const declared = declaredCaptures(chapter.steps)
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

export function checkGuide({ guideDir, chapters }) {
  const problems = []
  let blocks = 0
  let files = 0
  for (const name of chapterNames(guideDir)) {
    const present = LOCALES.filter((locale) => existsSync(join(guideDir, locale, name)))
    files += present.length
    if (present.length < LOCALES.length) continue
    const texts = readTexts(guideDir, name)
    const chapter = chapters.find((candidate) => candidate.file === name)
    problems.push(...chapterProblems({ name, texts, chapter, identity: true }))
    try {
      blocks += findBlocks(texts[LOCALES[0]]).length
    } catch (error) {
      if (!(error instanceof MarkerError)) throw error
    }
  }
  return { problems, blocks, files }
}
