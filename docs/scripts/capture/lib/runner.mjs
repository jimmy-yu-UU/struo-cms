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
//     deletes: 'revokes the token this run issued', // a DELETE of something the run did not create
//     verify: (json, vars) => {},   // throws to stop the run
//     transforms: ['stripTraceId'], // print transforms (lib/transforms.mjs)
//     print: { requestBody, note, headers, responseOnly, path, mask },
//                                   // how the exchange is shown. requestBody replaces the printed
//                                   // body; mask: { name: '<placeholder>' } replaces every occurrence
//                                   // of the secret registered under that name in the exchange text
//   }
//
// Secrets: the admin password (name adminPassword), every value a step saves with `secret`, and the
// cookie an identity holds (name session_<identity>; the guard's login included) are registered. An
// exchange that holds any of them is refused unless the step masks every one it holds.
//
// Rules the runner keeps: the fresh-database guard runs before the first step; a block that holds a
// secret value or a carriage return is refused; a DELETE must carry a whole path segment equal to an
// id a 201 response of this run returned, or a declared `deletes` reason; and the run produces and
// checks every block of every selected file before it replaces any file.
import { renameSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { LOCALES, chapterProblems, declaredByFile, readTexts } from './check.mjs'
import { ConnectionError, HostUnreachableError, RequestTimeoutError, createClient } from './client.mjs'
import { CaptureError } from './errors.mjs'
import { checkFresh } from './fresh.mjs'
import { formatBlock, formatExchange } from './format.mjs'
import { MarkerError, replaceBlock } from './markers.mjs'
import { inOrder } from './sequence.mjs'
import { TransformError, applyTransforms } from './transforms.mjs'

export { CaptureError }

const VARIABLE = /\{\{(\w+)\}\}/g
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
  if (value === undefined || value === null || String(value) === '') throw new Error(`the response has no value at '${dotted}'`)
  return String(value)
}

const UNMASKED_SECRET =
  'the block contains a value registered as secret during the run; declare print.mask for it'

function holdsSecret(text, secrets) {
  return [...secrets].some((secret) => secret !== '' && text.includes(secret))
}

const labelOf = (step) => step.capture ?? step.name ?? `${step.method} ${step.path}`

// Longest first, so a secret that contains another is replaced whole.
const longestFirst = (values) => [...values].sort((a, b) => b.length - a.length || a.localeCompare(b))

// An empty secret would match everywhere, so it is ignored.
function redact(text, secrets) {
  return longestFirst(secrets)
    .filter((secret) => secret !== '')
    .reduce((current, secret) => current.replaceAll(secret, '<secret>'), text)
}

function excerpt(text, state) {
  return redact(text, state.secrets).slice(0, EXCERPT)
}

function registerSecret(state, name, value) {
  state.secrets.add(value)
  state.secretByName.set(name, value)
}

// A row the run created is known by its id: the value saved from a path whose last key is `id` or
// ends in `Id`. A version, a name or any other field of a 201 response opens no DELETE.
const ID_PATH = /(?:^|\.)(?:id|\w+Id)$/

function save(state, entries, json, { secret, created }) {
  for (const [name, dotted] of Object.entries(entries ?? {})) {
    const value = pick(json, dotted)
    state.vars = { ...state.vars, [name]: value }
    if (created && ID_PATH.test(dotted)) state.created.add(value)
    if (secret) registerSecret(state, name, value)
  }
}

// The cookies the guard's own requests leave in a jar are live credentials like any other.
function registerSessions(state, client) {
  for (const [identity, cookies] of client.sessions()) {
    for (const cookie of cookies) registerSecret(state, `session_${identity}`, cookie)
  }
}

// The session cookie a response sets is a live credential: it is registered as a secret under
// session_<identity> the moment it arrives.
function registerCookies(state, identity, headers) {
  if (identity === undefined) return
  for (const [name, value] of headers) {
    if (name.toLowerCase() !== 'set-cookie') continue
    const pair = value.split(';')[0]
    const cookie = pair.slice(pair.indexOf('=') + 1).trim()
    if (cookie !== '') registerSecret(state, `session_${identity}`, cookie)
  }
}

// Replaces every secret the step names, longest value first. A secret the mask does not name is
// refused before anything is replaced: masking a value that sits inside a longer secret would
// otherwise leave the rest of the longer one in the text, unrecognisable as a secret.
function applyMask(text, mask, state) {
  const placeholders = new Map()
  for (const [name, placeholder] of Object.entries(mask ?? {})) {
    const value = state.secretByName.get(name)
    if (value === undefined) throw new Error(`print.mask names '${name}', which no step registered as secret`)
    placeholders.set(value, placeholder)
  }
  const unnamed = [...state.secrets].filter((secret) => !placeholders.has(secret))
  if (holdsSecret(text, unnamed)) throw new Error(UNMASKED_SECRET)
  return longestFirst(placeholders.keys()).reduce(
    (current, value) => current.replaceAll(value, placeholders.get(value)),
    text,
  )
}

function segmentsOf(path) {
  return path.split('?')[0].split('/').filter((segment) => segment !== '')
}

function requireDeletable(step, path, state) {
  if (step.method !== 'DELETE') return
  if (typeof step.deletes === 'string' && step.deletes.trim() !== '') return
  if (segmentsOf(path).some((segment) => state.created.has(segment))) return
  throw new Error(
    'DELETE refused: no path segment equals an id a 201 response of this run returned; declare `deletes` with the reason on the step',
  )
}

// Sends one step: refuses an unsafe DELETE before anything is sent, registers the cookies the
// response sets, and checks the declared status.
async function send(client, step, state, { path, body }) {
  requireDeletable(step, path, state)
  const response = await client.request({
    identity: step.as,
    method: step.method,
    path,
    body,
    bearer: step.bearer === undefined ? undefined : substitute(step.bearer, state.vars),
    csrf: step.csrf,
    timeoutMs: step.timeoutMs,
  })
  registerCookies(state, step.as, response.headers)
  if (response.status !== step.expect) {
    throw new Error(`expected HTTP ${step.expect}, got ${response.status}: ${excerpt(response.body, state)}`)
  }
  return response
}

// Takes the values a step saves from its JSON response and runs its verify hook.
function collect(state, step, response) {
  if (!step.save && !step.secret && !step.verify) return
  let json
  try {
    json = JSON.parse(response.body)
  } catch {
    throw new Error(`the response is not JSON: ${excerpt(response.body, state)}`)
  }
  const created = response.status === 201
  save(state, step.save, json, { secret: false, created })
  save(state, step.secret, json, { secret: true, created })
  step.verify?.(json, state.vars)
}

function printExchange(state, step, { path, body }, response) {
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
  const masked = applyMask(text, step.print?.mask, state)
  state.exchanges.set(step.capture, [...(state.exchanges.get(step.capture) ?? []), masked])
}

async function execute(client, step, state) {
  const request = { path: substitute(step.path, state.vars), body: requestBody(step, state.vars) }
  const response = await send(client, step, state, request)
  collect(state, step, response)
  if (step.capture) printExchange(state, step, request, response)
}

// Every failure of a step leaves as a CaptureError that says which capture, file and request it was.
async function runStep(client, step, state, context) {
  const where = `${labelOf(step)} (${context.file ?? context.group}, step ${context.index}: ${step.method} ${step.path})`
  try {
    await execute(client, step, state)
  } catch (error) {
    throw new CaptureError(`${where}: ${redact(error.message ?? String(error), state.secrets)}`, {
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
    if (holdsSecret(text, secrets)) {
      throw new CaptureError(
        `${id} (${file}): ${UNMASKED_SECRET}`,
        { capture: id, file },
      )
    }
  }
}

function writeTemporaries(pending, io) {
  try {
    pending.forEach(({ file, temporary }) => io.writeFileSync(temporary, file.content))
  } catch (error) {
    pending.forEach(({ temporary }) => io.rmSync(temporary, { force: true }))
    throw new CaptureError(`writing the temporary files failed (${error.message}); no chapter file was changed`)
  }
}

function renameFailure(pending, index, error) {
  const replaced = pending.slice(0, index).map((item) => item.file.label)
  const left = pending.slice(index).map((item) => item.file.label)
  return new CaptureError(
    `replacing ${pending[index].file.label} failed (${error.message}); already replaced: ${replaced.join(', ') || 'none'}; ` +
      `not replaced: ${left.join(', ')}; the two languages of a file are replaced one after the other`,
  )
}

// Writes every changed file next to its target first and renames them one after the other, a file's
// two languages adjacent, only when all are written. A failure while writing removes the temporary
// files and changes no chapter file. A failure while renaming removes the remaining temporary files
// and reports which files were already replaced, because replacing several files is not atomic.
function writeAll(files, io) {
  const pending = files.filter((file) => file.changed).map((file) => ({ file, temporary: `${file.path}.capture-tmp` }))
  writeTemporaries(pending, io)
  pending.forEach(({ file, temporary }, index) => {
    try {
      io.renameSync(temporary, file.path)
    } catch (error) {
      pending.slice(index).forEach((item) => io.rmSync(item.temporary, { force: true }))
      throw renameFailure(pending, index, error)
    }
  })
}

// Failures of the tool's own parts and of the file system are CaptureErrors; anything else is a bug
// and keeps its stack trace.
function operational(error) {
  return (
    error instanceof MarkerError ||
    error instanceof TransformError ||
    error instanceof HostUnreachableError ||
    error instanceof ConnectionError ||
    error instanceof RequestTimeoutError ||
    typeof error.code === 'string'
  )
}

// Reads the selected chapter files and checks their markers against the run, before any request.
function prepare({ groups, guideDir, only }) {
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
  return { declared, loaded, steps }
}

function newState(vars) {
  const password = vars.adminPassword
  return {
    vars: { ...vars },
    exchanges: new Map(),
    created: new Set(),
    secrets: new Set([password].filter(Boolean)),
    secretByName: new Map(password ? [['adminPassword', password]] : []),
  }
}

async function guarded(guard, args) {
  try {
    await guard(args)
  } catch (error) {
    if (error instanceof CaptureError) throw error
    throw new CaptureError(`fresh-database check: ${error.message}`)
  }
}

// Runs the guard and then every step, strictly one after another.
async function play({ baseUrl, vars, steps, guard, freshCollections }) {
  const client = createClient({ baseUrl })
  const state = newState(vars)
  try {
    await guarded(guard, { client, vars, collections: freshCollections })
    registerSessions(state, client)
    await inOrder(steps, ({ step, group, file }, index) => runStep(client, step, state, { group, file, index: index + 1 }))
  } finally {
    client.close()
  }
  return state
}

// The new text of every selected file, checked, with the block ids that belong to it.
function assemble({ declared, loaded, blocks, guideDir }) {
  return loaded.flatMap(({ file, texts }) => {
    const rewritten = rewrite(file, texts, declared.get(file), blocks)
    const after = chapterProblems({ name: file, texts: rewritten, declared: declared.get(file), identity: true })
    if (after.length > 0) throw new CaptureError(after.join('\n'))
    return LOCALES.map((locale) => ({
      path: join(guideDir, locale, file),
      label: `${locale}/${file}`,
      content: rewritten[locale],
      changed: rewritten[locale] !== texts[locale],
    }))
  })
}

async function run(options) {
  const prepared = prepare(options)
  const state = await play({ ...options, steps: prepared.steps })
  const blocks = new Map([...state.exchanges].map(([id, exchanges]) => [id, formatBlock(exchanges)]))
  const fileOf = new Map([...prepared.declared].flatMap(([file, ids]) => [...ids.keys()].map((id) => [id, file])))
  refuseUnsafeBlocks(blocks, state.secrets, fileOf)
  const files = assemble({ declared: prepared.declared, loaded: prepared.loaded, blocks, guideDir: options.guideDir })
  if (options.write) writeAll(files, options.io)
  return { blocks, files }
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
  try {
    return await run({ baseUrl, vars, groups, guideDir, write, only, guard, freshCollections, io })
  } catch (error) {
    if (error instanceof CaptureError || !operational(error)) throw error
    throw new CaptureError(error.message)
  }
}
