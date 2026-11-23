// Runs a capture: the seed steps, then the selected chapters' steps, in order, against a running
// host; formats each captured exchange; and replaces the marked blocks in both languages with the
// same text. Nothing here is specific to a chapter or to a collection.
//
// A scenario is an ordered list of steps. Each step is a plain object:
//
//   {
//     capture: 'ch10-some-id',      // printed; steps sharing an id form one block. Omit for a setup step.
//     name: 'create category',      // label for a setup step in messages
//     as: 'admin',                  // identity (cookie jar); omit for a request with no cookie
//     bearer: '{token}',            // a bearer token instead of the cookie
//     method: 'POST',
//     path: '/api/items/x/{xId}',   // {name} is replaced by a variable an earlier step saved
//     body: { a: '{xId}' },         // an object is sent as compact JSON; a string is sent verbatim;
//                                   // a function receives the variables and returns either
//     expect: 201,                  // required: the status the manual shows
//     csrf: false,                  // omit X-Struo-CSRF for this step
//     save: { xId: 'data.id' },     // variables taken from the JSON response by dotted path
//     verify: (json, vars) => {},   // throws a CaptureError to stop the run
//     transforms: ['stripTraceId'], // print transforms (lib/transforms.mjs), the only way output differs
//     print: { note, headers, responseOnly, path },  // how the exchange is shown (lib/format.mjs)
//   }
import { writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { LOCALES, chapterProblems, declaredCaptures, readTexts } from './check.mjs'
import { HostUnreachableError, createClient } from './client.mjs'
import { formatBlock, formatExchange } from './format.mjs'
import { replaceBlock } from './markers.mjs'
import { applyTransforms } from './transforms.mjs'

export class CaptureError extends Error {}

const VARIABLE = /\{([A-Za-z0-9_]+)\}/g
const EXCERPT = 300

function substitute(value, vars, label) {
  if (typeof value === 'string') {
    return value.replace(VARIABLE, (_, name) => {
      if (!(name in vars)) throw new CaptureError(`${label}: unknown variable '${name}'`)
      return vars[name]
    })
  }
  if (Array.isArray(value)) return value.map((item) => substitute(item, vars, label))
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, substitute(item, vars, label)]))
  }
  return value
}

function requestBody(step, vars, label) {
  const raw = typeof step.body === 'function' ? step.body(vars) : step.body
  if (raw === undefined || typeof raw === 'string') return raw
  return JSON.stringify(substitute(raw, vars, label))
}

function parseJson(response, label) {
  try {
    return JSON.parse(response.body)
  } catch {
    throw new CaptureError(`${label}: the response is not JSON: ${response.body.slice(0, EXCERPT)}`)
  }
}

function pick(json, dotted, label) {
  const value = dotted.split('.').reduce((current, key) => current?.[key], json)
  if (value === undefined || value === null) throw new CaptureError(`${label}: the response has no '${dotted}'`)
  return String(value)
}

function labelOf(step) {
  return step.capture ?? step.name ?? `${step.method} ${step.path}`
}

async function runStep(client, step, state) {
  const label = labelOf(step)
  const path = substitute(step.path, state.vars, label)
  const body = requestBody(step, state.vars, label)
  const bearer = step.bearer === undefined ? undefined : substitute(step.bearer, state.vars, label)
  const response = await client.request({ identity: step.as, method: step.method, path, body, bearer, csrf: step.csrf })
  if (response.status !== step.expect) {
    throw new CaptureError(
      `${label}: expected HTTP ${step.expect}, got ${response.status} for ${step.method} ${path}: ${response.body.slice(0, EXCERPT)}`,
    )
  }
  if (step.save || step.verify) {
    const json = parseJson(response, label)
    for (const [name, dotted] of Object.entries(step.save ?? {})) state.vars = { ...state.vars, [name]: pick(json, dotted, label) }
    step.verify?.(json, state.vars)
  }
  if (step.capture) {
    const printed = { ...response, body: applyTransforms(response.body, step.transforms) }
    const text = formatExchange({
      method: step.method,
      path,
      requestBody: body,
      printPath: step.print?.path,
      note: step.print?.note,
      headers: step.print?.headers,
      responseOnly: step.print?.responseOnly,
      response: printed,
    })
    state.exchanges.set(step.capture, [...(state.exchanges.get(step.capture) ?? []), text])
  }
}

function requireStatuses(steps) {
  for (const step of steps) {
    if (!Number.isInteger(step.expect)) throw new CaptureError(`${labelOf(step)}: the step declares no expected status`)
  }
}

function rewrite(chapter, texts, blocks) {
  return Object.fromEntries(
    LOCALES.map((locale) => {
      const file = `${locale}/${chapter.file}`
      const text = [...declaredCaptures(chapter.steps).keys()].reduce(
        (current, id) => replaceBlock(current, id, blocks.get(id), { file }),
        texts[locale],
      )
      return [locale, text]
    }),
  )
}

export async function runCapture({ baseUrl, vars, seed, chapters, guideDir, write }) {
  const loaded = chapters.map((chapter) => ({ chapter, texts: readTexts(guideDir, chapter.file) }))
  const problems = loaded.flatMap(({ chapter, texts }) =>
    chapterProblems({ name: chapter.file, texts, chapter, identity: false }),
  )
  if (problems.length > 0) throw new CaptureError(problems.join('\n'))

  const steps = [...seed, ...chapters.flatMap((chapter) => chapter.steps)]
  requireStatuses(steps)

  const client = createClient({ baseUrl })
  const state = { vars: { ...vars }, exchanges: new Map() }
  try {
    for (const step of steps) await runStep(client, step, state)
  } catch (error) {
    if (error instanceof HostUnreachableError) throw new CaptureError(error.message)
    throw error
  } finally {
    client.close()
  }

  const blocks = new Map([...state.exchanges].map(([id, exchanges]) => [id, formatBlock(exchanges)]))
  const files = []
  for (const { chapter, texts } of loaded) {
    const rewritten = rewrite(chapter, texts, blocks)
    const after = chapterProblems({ name: chapter.file, texts: rewritten, chapter, identity: true })
    if (after.length > 0) throw new CaptureError(after.join('\n'))
    for (const locale of LOCALES) {
      files.push({ path: join(guideDir, locale, chapter.file), content: rewritten[locale], changed: rewritten[locale] !== texts[locale] })
    }
  }
  if (write) for (const file of files) writeFileSync(file.path, file.content)
  return { blocks, files }
}
