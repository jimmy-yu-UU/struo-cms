// Runs a capture: the groups of steps, in order, against a running host; formats each captured
// exchange; and replaces the marked blocks in both languages with the same text. Nothing here is
// specific to a chapter or to a collection.
//
// A run is one ordered list of groups. A group is { name, file?, steps }; the order of the list is
// the order the data set is built and changed in, and it never depends on which files are written.
// A capture step names the chapter file its block lives in, or takes the file of its group, so one
// file can be fed from several places in the run. `only` chooses the files that are written; the
// other files are not read, and their blocks are produced and discarded.
//
// A step is a plain object:
//
//   {
//     capture: 'ch10-some-id',      // printed; steps sharing an id form one block. Omit for a setup step.
//     file: '10-query-basics.md',   // chapter file of the block; defaults to the group's file
//     name: 'create category',      // label for a setup step in messages
//     as: 'admin',                  // identity (cookie jar); omit for a request with no cookie
//     bearer: '{{token}}',          // a bearer token instead of the cookie
//     method: 'POST',
//     path: '/api/items/x/{{xId}}', // {{name}} is replaced by a variable an earlier step saved
//     body: { a: '{{xId}}' },       // an object is sent as compact JSON; a string is sent verbatim;
//                                   // a function receives the variables and returns either
//     expect: 201,                  // required: the status the manual shows
//     csrf: false,                  // omit X-Struo-CSRF for this step
//     timeoutMs: 120000,            // overrides the client's request timeout
//     save: { xId: 'data.id' },     // variables taken from the JSON response by dotted path
//     secret: { token: 'data.token' }, // like save; the value may never appear in a written block
//     verify: (json, vars) => {},   // throws to stop the run
//     transforms: ['stripTraceId'], // print transforms (lib/transforms.mjs)
//     print: { requestBody, note, headers, responseOnly, path },  // how the exchange is shown;
//                                   // requestBody replaces the printed body, e.g. a placeholder
//                                   // where the sent body holds the admin password
//   }
//
// Rules the runner keeps: the fresh-database guard runs before the first step; a block that holds a
// secret value or a carriage return is refused; a DELETE must target a value the run itself saved
// from a response; and no chapter file changes unless every selected file was produced and checked.
import { renameSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { LOCALES, chapterProblems, declaredByFile, readTexts } from './check.mjs'
import { createClient } from './client.mjs'
import { CaptureError } from './errors.mjs'
import { checkFresh } from './fresh.mjs'
import { formatBlock, formatExchange } from './format.mjs'
import { replaceBlock } from './markers.mjs'
import { applyTransforms } from './transforms.mjs'

export { CaptureError }

const VARIABLE = /\{\{([A-Za-z0-9_]+)\}\}/g
const EXCERPT = 300
const DEFAULT_IO = { writeFileSync, renameSync, rmSync }

function substitute(value, vars) {
  if (typeof value === 'string') {
    return value.replace(VARIABLE, (_, name) => {
      if (!(name in vars)) throw new Error(`unknown variable '${name}'`)
      return vars[name]
    })
  }
  if (Array.isArray(value)) return value.map((item) => substitute(item, vars))
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, substitute(item, vars)]))
  }
  return value
}

function requestBody(step, vars) {
  const raw = typeof step.body === 'function' ? step.body(vars) : step.body
  if (raw === undefined || typeof raw === 'string') return raw
  return JSON.stringify(substitute(raw, vars))
}

function pick(json, dotted) {
  const value = dotted.split('.').reduce((current, key) => current?.[key], json)
  if (value === undefined || value === null) throw new Error(`the response has no '${dotted}'`)
  return String(value)
}

const labelOf = (step) => step.capture ?? step.name ?? `${step.method} ${step.path}`

function redact(text, secrets) {
  return [...secrets].reduce((current, secret) => current.split(secret).join('<secret>'), text)
}

function save(state, entries, json, secret) {
  for (const [name, dotted] of Object.entries(entries ?? {})) {
    const value = pick(json, dotted)
    state.vars = { ...state.vars, [name]: value }
    state.saved.add(value)
    if (secret) state.secrets.add(value)
  }
}

async function execute(client, step, state) {
  const path = substitute(step.path, state.vars)
  const body = requestBody(step, state.vars)
  const bearer = step.bearer === undefined ? undefined : substitute(step.bearer, state.vars)
  if (step.method === 'DELETE' && ![...state.saved].some((value) => path.includes(value))) {
    throw new Error(
      'DELETE refused: the path contains no value this run saved from a response, and a run removes only what it created',
    )
  }
  const response = await client.request({
    identity: step.as,
    method: step.method,
    path,
    body,
    bearer,
    csrf: step.csrf,
    timeoutMs: step.timeoutMs,
  })
  if (response.status !== step.expect) {
    throw new Error(`expected HTTP ${step.expect}, got ${response.status}: ${response.body.slice(0, EXCERPT)}`)
  }
  if (step.save || step.secret || step.verify) {
    let json
    try {
      json = JSON.parse(response.body)
    } catch {
      throw new Error(`the response is not JSON: ${response.body.slice(0, EXCERPT)}`)
    }
    save(state, step.save, json, false)
    save(state, step.secret, json, true)
    step.verify?.(json, state.vars)
  }
  if (step.capture) {
    const text = formatExchange({
      method: step.method,
      path,
      requestBody: step.print?.requestBody ?? body,
      printPath: step.print?.path,
      note: step.print?.note,
      headers: step.print?.headers,
      responseOnly: step.print?.responseOnly,
      response: { ...response, body: applyTransforms(response.body, step.transforms) },
    })
    state.exchanges.set(step.capture, [...(state.exchanges.get(step.capture) ?? []), text])
  }
}

// Every failure of a step leaves as a CaptureError that says which capture, file and request it was.
async function runStep(client, step, state, context) {
  const where = `${labelOf(step)} (${context.file ?? context.group}, step ${context.index}: ${step.method} ${step.path})`
  try {
    await execute(client, step, state)
  } catch (error) {
    const reason = error.message ?? String(error)
    throw new CaptureError(`${where}: ${redact(reason, state.secrets)}`, {
      capture: step.capture,
      file: context.file,
      step: context.index,
    })
  }
}

function rewrite(file, texts, declared, blocks) {
  return Object.fromEntries(
    LOCALES.map((locale) => {
      const label = `${locale}/${file}`
      const text = [...declared.keys()].reduce(
        (current, id) => replaceBlock(current, id, blocks.get(id), { file: label }),
        texts[locale],
      )
      return [locale, text]
    }),
  )
}

function refuseUnsafeBlocks(blocks, secrets, files) {
  for (const [id, text] of blocks) {
    const file = files.get(id)
    if (text.includes('\r')) throw new CaptureError(`${id} (${file}): the captured text contains a carriage return`, { capture: id, file })
    if ([...secrets].some((secret) => secret !== '' && text.includes(secret))) {
      throw new CaptureError(
        `${id} (${file}): the block contains a value registered as secret during the run; declare a print override for it`,
        { capture: id, file },
      )
    }
  }
}

// Writes every changed file next to its target first and renames them only when all are written, so
// a failure while writing leaves no chapter file changed and no temporary file behind.
function writeAll(files, io) {
  const pending = files.filter((file) => file.changed).map((file) => ({ file, temporary: `${file.path}.capture-tmp` }))
  try {
    for (const { file, temporary } of pending) io.writeFileSync(temporary, file.content)
  } catch (error) {
    for (const { temporary } of pending) io.rmSync(temporary, { force: true })
    throw error
  }
  for (const { file, temporary } of pending) io.renameSync(temporary, file.path)
}

export async function runCapture({
  baseUrl,
  vars,
  groups,
  guideDir,
  write,
  only,
  guard = checkFresh,
  freshCollections = [],
  io = DEFAULT_IO,
}) {
  const declared = declaredByFile(groups)
  const selected = only ?? [...declared.keys()]
  const loaded = selected.map((file) => {
    if (!declared.has(file)) throw new CaptureError(`${file}: no capture in the scenario writes into this file`)
    return { file, texts: readTexts(guideDir, file) }
  })
  const problems = loaded.flatMap(({ file, texts }) =>
    chapterProblems({ name: file, texts, declared: declared.get(file), identity: false }),
  )
  if (problems.length > 0) throw new CaptureError(problems.join('\n'))

  const steps = groups.flatMap((group) =>
    group.steps.map((step) => ({ step, group: group.name, file: step.file ?? group.file })),
  )
  for (const { step } of steps) {
    if (!Number.isInteger(step.expect)) throw new CaptureError(`${labelOf(step)}: the step declares no expected status`)
  }

  const client = createClient({ baseUrl })
  const state = {
    vars: { ...vars },
    exchanges: new Map(),
    saved: new Set(),
    secrets: new Set([vars.adminPassword].filter(Boolean)),
  }
  try {
    try {
      await guard({ client, vars, collections: freshCollections })
    } catch (error) {
      if (error instanceof CaptureError) throw error
      throw new CaptureError(`fresh-database check: ${error.message}`)
    }
    let index = 0
    for (const { step, group, file } of steps) {
      index += 1
      await runStep(client, step, state, { group, file, index })
    }
  } finally {
    client.close()
  }

  const blocks = new Map([...state.exchanges].map(([id, exchanges]) => [id, formatBlock(exchanges)]))
  const fileOf = new Map([...declared].flatMap(([file, ids]) => [...ids.keys()].map((id) => [id, file])))
  refuseUnsafeBlocks(blocks, state.secrets, fileOf)

  const files = []
  for (const { file, texts } of loaded) {
    const rewritten = rewrite(file, texts, declared.get(file), blocks)
    const after = chapterProblems({ name: file, texts: rewritten, declared: declared.get(file), identity: true })
    if (after.length > 0) throw new CaptureError(after.join('\n'))
    for (const locale of LOCALES) {
      files.push({ path: join(guideDir, locale, file), content: rewritten[locale], changed: rewritten[locale] !== texts[locale] })
    }
  }
  if (write) writeAll(files, io)
  return { blocks, files }
}
