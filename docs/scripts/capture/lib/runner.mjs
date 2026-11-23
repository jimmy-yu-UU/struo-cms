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
//     upload: { field, fixture, contentType }, // a multipart file part from the fixtures directory
//     body: { a: '{{xId}}' },       // an object is sent as compact JSON; a string is sent verbatim;
//                                   // a function receives the variables and returns either
//     expect: 201,                  // required: the status the manual shows
//     csrf: false,                  // omit X-Struo-CSRF for this step
//     timeoutMs: 120000,            // overrides the client's request timeout
//     save: { xId: 'data.id' },     // variables taken from the JSON response by dotted path
//     secret: { token: 'data.token' }, // like save; the value may never appear in a written block
//     deletes: 'revokes the token this run issued', // a DELETE of something the run did not create
//     verify: (json, vars) => {},   // throws to stop the run
//     verifyResponse: (response) => {}, // the raw response (status, headers, bytes); throws to stop the run
//     transforms: ['stripTraceId'], // print transforms (lib/transforms.mjs)
//     cli: { program, args, env, exit }, // a command the operator runs; the step sends nothing (lib/cli.mjs)
//     print: { requestBody, note, headers, responseOnly, path, pretty, binary, mask },
//                                   // how the exchange is shown. requestBody replaces the printed
//                                   // body (a string, an object or a function of the variables); note and
//                                   // path take {{name}}; headers: true or a list of names; pretty adds a
//                                   // `pretty:` copy; binary: 'png' | true prints a summary of the bytes
//                                   // instead of the body; mask: { name: '<placeholder>' } replaces every occurrence
//                                   // of the secret registered under that name in the exchange text
//   }
//
// Secrets: the admin password (name adminPassword), every value a step saves with `secret`, and the
// cookie an identity holds (name session_<identity>; session_anonymous for a step with no identity;
// the guard's login included) are registered. An exchange that holds any of them is refused unless
// the step masks every one it holds.
//
// Rules the runner keeps: the fresh-database guard runs before the first step; a block that holds a
// secret value or a carriage return is refused; a DELETE must carry a whole path segment equal to an
// id a 201 response of this run returned, or a declared `deletes` reason; and the run produces and
// checks every block of every selected file before it replaces any file.
import { readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { BinaryError, summarizeBinary } from './binary.mjs'
import { cliBlock, cliDescriptors, readCliResult } from './cli.mjs'
import { LOCALES, chapterProblems, declaredByFile, readTexts } from './check.mjs'
import { ConnectionError, HostUnreachableError, RequestTimeoutError, createClient, ownedHeaders } from './client.mjs'
import { CaptureError } from './errors.mjs'
import { checkFresh } from './fresh.mjs'
import { formatBlock, formatExchange, shownHeaders } from './format.mjs'
import { MarkerError, replaceBlock } from './markers.mjs'
import { MultipartError, buildMultipart } from './multipart.mjs'
import { inOrder } from './sequence.mjs'
import { TransformError, applyTransforms } from './transforms.mjs'

export { CaptureError }

const VARIABLE = /\{\{(\w+)\}\}/g
const EXCERPT = 300
const ANONYMOUS = 'anonymous'
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

function renderBody(source, vars) {
  const raw = typeof source === 'function' ? source(vars) : source
  if (raw === undefined || typeof raw === 'string') return raw
  return JSON.stringify(substitute(raw, vars))
}

const FIXTURE_NAME = /^\w[\w.-]*$/

function readFixture(dir, name) {
  if (!dir) throw new Error('an upload step needs a fixtures directory')
  if (!FIXTURE_NAME.test(name)) throw new Error(`fixture '${name}' is not a plain file name`)
  return readFileSync(join(dir, name))
}

// The bytes and content type a step sends: an upload builds its multipart body from the fixture.
function outgoing(step, vars, fixtures) {
  if (!step.upload) {
    const body = renderBody(step.body, vars)
    const override = step.print?.requestBody
    return { body, contentType: undefined, printed: override === undefined ? body : renderBody(override, vars) }
  }
  const { field, fixture, contentType: partType } = step.upload
  const { body, contentType } = buildMultipart({ field, fileName: fixture, partType, content: fixtures.get(fixture) })
  return { body, contentType, printed: renderBody(step.print?.requestBody, vars) }
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

const requestOf = (step) => (step.cli ? `command ${step.cli.args.join(' ')}` : `${step.method} ${step.path}`)
const labelOf = (step) => step.capture ?? step.name ?? requestOf(step)

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
  state.secretByName.set(name, new Set([...(state.secretByName.get(name) ?? []), value]))
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
  const holder = identity ?? ANONYMOUS
  for (const [name, value] of headers) {
    if (name.toLowerCase() !== 'set-cookie') continue
    const pair = value.split(';')[0]
    const cookie = pair.slice(pair.indexOf('=') + 1).trim()
    if (cookie !== '') registerSecret(state, `session_${holder}`, cookie)
  }
}

// Replaces every secret the step names, longest value first. A secret the mask does not name is
// refused before anything is replaced: masking a value that sits inside a longer secret would
// otherwise leave the rest of the longer one in the text, unrecognisable as a secret.
function applyMask(text, mask, state) {
  const placeholders = new Map()
  for (const [name, placeholder] of Object.entries(mask ?? {})) {
    const values = state.secretByName.get(name)
    if (values === undefined) throw new Error(`print.mask names '${name}', which no step registered as secret`)
    for (const value of values) placeholders.set(value, placeholder)
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
async function send(client, step, state, { path, body, contentType }) {
  requireDeletable(step, path, state)
  const response = await client.request({
    identity: step.as,
    method: step.method,
    path,
    body,
    contentType,
    bearer: step.bearer === undefined ? undefined : substitute(step.bearer, state.vars),
    csrf: step.csrf,
    headers: step.headers,
    timeoutMs: step.timeoutMs,
  })
  registerCookies(state, step.as, response.headers)
  if (response.status !== step.expect) {
    throw new Error(`expected HTTP ${step.expect}, got ${response.status}: ${excerpt(response.body, state)}`)
  }
  return response
}

// Takes the values a step saves from its JSON response and runs its verify hook.
function parseJson(state, response) {
  try {
    return JSON.parse(response.body)
  } catch {
    throw new Error(`the response is not JSON: ${excerpt(response.body, state)}`)
  }
}

// Values the step saves, and the secrets it registers, are taken before any hook runs, so a hook that
// throws with the response in its message is redacted with them.
function collect(state, step, response) {
  const needsJson = Boolean(step.save || step.secret || step.verify)
  const json = needsJson ? parseJson(state, response) : undefined
  if (needsJson) {
    const created = response.status === 201
    save(state, step.save, json, { secret: false, created })
    save(state, step.secret, json, { secret: true, created })
  }
  step.verifyResponse?.(response)
  step.verify?.(json, state.vars)
}

// A step whose printed body differs from the one the server sent (a transform or a mask changed it)
// cannot also print the length the server sent: the two would disagree on the page. A binary
// summary prints no body and states the byte count itself, so it is exempt (printedText undefined).
function refuseStaleLength(step, response, printedText) {
  if (printedText === undefined || printedText === response.body || !step.print?.headers) return
  const shown = shownHeaders(response, step.print.headers)
  if (shown.some(([name]) => name.toLowerCase() === 'content-length')) {
    throw new Error(
      'the step shows Content-Length but the body it prints differs from the server body (a transform or a mask changes it); name the headers to show without it',
    )
  }
}

function uploadNote(step) {
  return step.upload ? `(multipart: file=@${step.upload.fixture}; type=${step.upload.contentType})` : undefined
}

function printedBody(step, response) {
  if (!step.print?.binary) return { body: applyTransforms(response.body, step.transforms), binary: undefined }
  if ((step.transforms ?? []).length > 0) throw new Error('print.binary prints no body, so a transform on the same step cannot apply')
  return { body: '', binary: summarizeBinary(response.bytes, step.print.binary) }
}

function printExchange(state, step, { path, printed }, response) {
  const { body, binary } = printedBody(step, response)
  refuseStaleLength(step, response, binary ? undefined : applyMask(body, step.print?.mask, state))
  const substituted = (value) => (value === undefined ? undefined : substitute(value, state.vars))
  const text = formatExchange({
    method: step.method,
    path,
    requestBody: printed,
    printPath: substituted(step.print?.path),
    note: [uploadNote(step), substituted(step.print?.note)].filter(Boolean).join(' ') || undefined,
    headers: step.print?.headers,
    responseOnly: step.print?.responseOnly,
    pretty: step.print?.pretty,
    binary,
    response: { ...response, body },
  })
  const masked = applyMask(text, step.print?.mask, state)
  state.exchanges.set(step.capture, [...(state.exchanges.get(step.capture) ?? []), masked])
}

async function execute(client, step, state, context) {
  const request = { path: substitute(step.path, state.vars), ...outgoing(step, state.vars, context.fixtures) }
  const response = await send(client, step, state, request)
  collect(state, step, response)
  if (step.capture) printExchange(state, step, request, response)
}

// A command step sends nothing: its block comes from the result files read before the run.
function recordCommand(state, step, context) {
  const text = context.commands.get(step.capture)
  if (text !== undefined) state.exchanges.set(step.capture, [text])
}

// Every failure of a step leaves as a CaptureError that says which capture, file and request it was.
async function runStep(client, step, state, context) {
  const where = `${labelOf(step)} (${context.file ?? context.group}, step ${context.index}: ${requestOf(step)})`
  try {
    if (step.cli) recordCommand(state, step, context)
    else await execute(client, step, state, context)
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
    error instanceof BinaryError ||
    error instanceof MultipartError ||
    error instanceof HostUnreachableError ||
    error instanceof ConnectionError ||
    error instanceof RequestTimeoutError ||
    typeof error.code === 'string'
  )
}

// Reads the selected chapter files and checks their markers against the run, before any request.
function prepare({ groups, guideDir, only, fixturesDir, cliResults }) {
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
  for (const { step } of steps.filter(({ step: candidate }) => !candidate.cli)) {
    if (!Number.isInteger(step.expect)) throw new CaptureError(`${labelOf(step)}: the step declares no expected status`)
  }
  checkRequestHeaders(steps)
  const fixtures = loadFixtures(steps, fixturesDir)
  const commands = loadCommands(groups, selected, cliResults)
  return { declared, loaded, steps, fixtures, commands }
}

// Extra request headers: never one the client owns, and a printed step says in its note that the
// request was not a plain one. Checked before any request.
function checkRequestHeaders(steps) {
  for (const { step } of steps.filter(({ step: candidate }) => candidate.headers)) {
    const owned = ownedHeaders(step.headers)
    if (owned.length > 0) throw new CaptureError(`${labelOf(step)}: a step cannot set the request header ${owned.join(', ')}`)
    if (step.capture && !step.print?.note) {
      throw new CaptureError(`${labelOf(step)}: a captured step with request headers must declare print.note, so the reader sees the request was not a plain one`)
    }
  }
}

// The fixture files the upload steps name, read before any request is sent.
function loadFixtures(steps, fixturesDir) {
  const fixtures = new Map()
  for (const { step } of steps.filter(({ step: candidate }) => candidate.upload)) {
    const name = step.upload.fixture
    try {
      fixtures.set(name, readFixture(fixturesDir, name))
    } catch (error) {
      throw new CaptureError(`${labelOf(step)}: ${error.message}`)
    }
  }
  return fixtures
}

// The saved output of every command step whose file is written. Every one must have its result.
function loadCommands(groups, selected, cliResults) {
  const wanted = cliDescriptors(groups).filter((descriptor) => selected.includes(descriptor.file))
  if (wanted.length > 0 && !cliResults) {
    throw new CaptureError(`${wanted.map((item) => item.id).join(', ')}: the run needs the output of a command; pass --cli-results <dir>`)
  }
  return new Map(wanted.map((descriptor) => [descriptor.id, cliBlock(descriptor, readCliResult(cliResults, descriptor))]))
}

function newState(vars, secretVars) {
  const declared = secretVars.map((name) => {
    if (typeof vars[name] !== 'string' || vars[name] === '') throw new CaptureError(`secretVars names '${name}', which no run variable holds`)
    return [name, vars[name]]
  })
  const named = [...(vars.adminPassword ? [['adminPassword', vars.adminPassword]] : []), ...declared]
  return {
    vars: { ...vars },
    exchanges: new Map(),
    created: new Set(),
    secrets: new Set(named.map(([, value]) => value)),
    secretByName: new Map(named.map(([name, value]) => [name, new Set([value])])),
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
async function play({ baseUrl, vars, steps, guard, freshCollections, secretVars, fixtures, commands }) {
  const client = createClient({ baseUrl })
  const state = newState(vars, secretVars)
  try {
    await guarded(guard, { client, vars, collections: freshCollections })
    registerSessions(state, client)
    await inOrder(steps, ({ step, group, file }, index) => runStep(client, step, state, { group, file, index: index + 1, fixtures, commands }))
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
  const state = await play({ ...options, steps: prepared.steps, fixtures: prepared.fixtures, commands: prepared.commands })
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
  secretVars = [],
  fixturesDir,
  cliResults,
  io = DEFAULT_IO,
}) {
  try {
    return await run({ baseUrl, vars, groups, guideDir, write, only, guard, freshCollections, secretVars, fixturesDir, cliResults, io })
  } catch (error) {
    if (error instanceof CaptureError || !operational(error)) throw error
    throw new CaptureError(error.message)
  }
}
