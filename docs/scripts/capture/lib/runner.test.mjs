import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, readdirSync, renameSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { test } from 'node:test'
import { FIXTURE_COOKIE, FIXTURE_PASSWORD, FIXTURE_TOKEN } from '../test-support/fixtures.mjs'
import { startStub } from '../test-support/stub-server.mjs'
import { CaptureError, runCapture } from './runner.mjs'

const BLOCK = (id, body = 'old', fence = '```text') => `<!-- capture: ${id} -->\n${fence}\n${body}\n\`\`\`\n`
const noGuard = async () => {}
const DEMO = '01-demo.md'
const OTHER = '02-other.md'
const STATUS_OK = 'HTTP_STATUS:200'

// files: { [DEMO]: { zh, en } }
function makeGuide(files) {
  const root = mkdtempSync(join(tmpdir(), 'struo-capture-'))
  mkdirSync(join(root, 'zh-TW'))
  mkdirSync(join(root, 'en'))
  for (const [name, { zh, en }] of Object.entries(files)) {
    writeFileSync(join(root, 'zh-TW', name), zh)
    writeFileSync(join(root, 'en', name), en)
  }
  return root
}

const same = (text) => ({ zh: text, en: text })
const read = (root, dir, name = DEMO) => readFileSync(join(root, dir, name), 'utf8')
const json = (status, data) => ({ status, body: JSON.stringify({ success: true, data }) })

const api = (request) => {
  if (request.path === '/list') return { status: 200, body: '{"success":true,"data":[1],"meta":{"total":1}}' }
  if (request.path === '/created') return json(201, { id: 'abc-1' })
  if (request.path === '/read') return json(200, { id: 'read-9' })
  if (request.path === '/empty') return json(201, { id: '' })
  if (request.path === '/token') return json(201, { token: FIXTURE_TOKEN })
  if (request.path === '/login') {
    return { status: 200, headers: { 'Set-Cookie': `sess=${FIXTURE_COOKIE}; path=/` }, body: '{"success":true}' }
  }
  if (request.path === '/echo-password') return { status: 200, body: JSON.stringify({ echo: FIXTURE_PASSWORD }) }
  if (request.path === '/long-error') return { status: 500, body: `${'a'.repeat(290)}${FIXTURE_PASSWORD}tail` }
  if (request.path === '/cut') return { cut: '{"partial":' }
  if (request.path.startsWith('/item/abc-1')) return json(200, 'got')
  if (request.path === '/bad') return { status: 400, body: `{"error":{"code":"X","traceId":"${'f'.repeat(32)}"}}` }
  if (request.path === '/crlf') return { status: 200, body: 'a\r\nb' }
  return { status: 200, body: '{"ok":true}' }
}

async function run(root, groups, over = {}, handler = api) {
  const stub = await startStub(handler)
  try {
    const result = await runCapture({
      baseUrl: stub.baseUrl,
      vars: { adminPassword: FIXTURE_PASSWORD },
      groups,
      guideDir: root,
      write: false,
      guard: noGuard,
      ...over,
    })
    return { ...result, stub }
  } catch (error) {
    error.stub = stub
    throw error
  } finally {
    await stub.close()
  }
}

const demo = (steps) => [{ name: 'demo', file: DEMO, steps }]
const list = { capture: 'demo-list', method: 'GET', path: '/list', expect: 200 }
const created = { method: 'POST', path: '/created', expect: 201, save: { itemId: 'data.id' } }
const cleanup = (root) => rmSync(root, { recursive: true, force: true })

test('captures are written into both languages with the same text', async () => {
  const root = makeGuide({ [DEMO]: same(`# 一\n\n${BLOCK('demo-list')}`) })
  try {
    const result = await run(root, demo([list]), { write: true })
    const expected = '$ GET /list\n{"success":true,"data":[1],"meta":{"total":1}}\nHTTP_STATUS:200'
    for (const dir of ['zh-TW', 'en']) assert.ok(read(root, dir).includes(`\`\`\`text\n${expected}\n\`\`\``))
    assert.equal(result.blocks.get('demo-list'), expected)
  } finally {
    cleanup(root)
  }
})

test('without write nothing on disk changes', async () => {
  const text = BLOCK('demo-list')
  const root = makeGuide({ [DEMO]: same(text) })
  try {
    await run(root, demo([list]))
    assert.equal(read(root, 'en'), text)
  } finally {
    cleanup(root)
  }
})

test('{{name}} takes ids from earlier responses into paths, bodies and the printed line; single braces are text', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-get') + BLOCK('demo-gql')) })
  try {
    const steps = [
      created,
      { capture: 'demo-get', method: 'GET', path: '/item/{{itemId}}?x=1', expect: 200 },
      {
        capture: 'demo-gql',
        method: 'POST',
        path: '/graphql?q={total}',
        body: { query: 'query{articles{total}}', literal: 'itemId', id: '{{itemId}}' },
        expect: 200,
      },
    ]
    const result = await run(root, demo(steps))
    assert.ok(result.blocks.get('demo-get').startsWith('$ GET /item/abc-1?x=1\n'))
    assert.equal(result.stub.requests[2].path, '/graphql?q={total}')
    assert.equal(result.stub.requests[2].body, '{"query":"query{articles{total}}","literal":"itemId","id":"abc-1"}')
  } finally {
    cleanup(root)
  }
})

test('an unknown {{name}} is an error that names the capture, the file and the request', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-list')) })
  try {
    await assert.rejects(run(root, demo([{ ...list, path: '/item/{{nope}}' }])), (error) => {
      assert.ok(error instanceof CaptureError)
      assert.match(error.message, /demo-list.*01-demo\.md.*GET \/item\/\{\{nope\}\}.*unknown variable 'nope'/)
      assert.equal(error.capture, 'demo-list')
      return true
    })
  } finally {
    cleanup(root)
  }
})

test('a saved value that is empty or missing is an error that names the step', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-list')) })
  try {
    const empty = { name: 'save an empty id', method: 'POST', path: '/empty', expect: 201, save: { x: 'data.id' } }
    await assert.rejects(run(root, demo([empty, list])), /save an empty id.*no value at 'data\.id'/)
    const missing = { ...empty, name: 'save a missing id', path: '/created', save: { x: 'data.nope' } }
    await assert.rejects(run(root, demo([missing, list])), /save a missing id.*no value at 'data\.nope'/)
  } finally {
    cleanup(root)
  }
})

test('a request body object is sent compact and printed exactly as sent', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-post')) })
  try {
    const result = await run(root, demo([{ capture: 'demo-post', method: 'POST', path: '/created', body: { a: 1, b: ['x'] }, expect: 201 }]))
    assert.ok(result.blocks.get('demo-post').includes('body:\n{"a":1,"b":["x"]}\n'))
    assert.equal(result.stub.requests[0].body, '{"a":1,"b":["x"]}')
  } finally {
    cleanup(root)
  }
})

test('a declared request-print override changes the printed body only', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-login')) })
  try {
    const step = {
      capture: 'demo-login',
      method: 'POST',
      path: '/created',
      body: (vars) => ({ phrase: vars.adminPassword }),
      print: { requestBody: '{"phrase":"<your-admin-password>"}' },
      expect: 201,
    }
    const result = await run(root, demo([step]))
    assert.ok(result.blocks.get('demo-login').includes('body:\n{"phrase":"<your-admin-password>"}\n'))
    assert.equal(result.stub.requests[0].body, JSON.stringify({ phrase: FIXTURE_PASSWORD }))
  } finally {
    cleanup(root)
  }
})

test('a block that holds the admin password is refused with its capture id, and the password is never echoed', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-leak')) })
  try {
    const leak = { capture: 'demo-leak', method: 'POST', path: '/created', body: (vars) => ({ phrase: vars.adminPassword }), expect: 201 }
    await assert.rejects(run(root, demo([leak]), { write: true }), (error) => {
      assert.match(error.message, /demo-leak.*secret.*mask/)
      assert.ok(!error.message.includes(FIXTURE_PASSWORD))
      return true
    })
    assert.equal(read(root, 'en'), BLOCK('demo-leak'))
    await assert.rejects(
      run(root, demo([{ capture: 'demo-leak', method: 'GET', path: '/echo-password', expect: 200 }])),
      /demo-leak.*secret/,
    )
  } finally {
    cleanup(root)
  }
})

test('an error excerpt is redacted before it is cut, so no secret prefix survives at the boundary', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-list')) })
  try {
    await assert.rejects(run(root, demo([{ capture: 'demo-list', method: 'GET', path: '/long-error', expect: 200 }])), (error) => {
      assert.match(error.message, /expected HTTP 200, got 500/)
      assert.ok(!error.message.includes(FIXTURE_PASSWORD.slice(0, 4)))
      assert.ok(error.message.includes('<secret>'))
      return true
    })
  } finally {
    cleanup(root)
  }
})

test('an empty secret is ignored, so it can neither match nor mask anything', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-list')) })
  try {
    const result = await run(root, demo([list]), { vars: { adminPassword: '' } })
    assert.ok(result.blocks.get('demo-list').includes(STATUS_OK))
  } finally {
    cleanup(root)
  }
})

test('a value a step registers as secret is refused in any later block, and usable as a bearer token', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-token')) })
  try {
    const issue = { method: 'POST', path: '/token', expect: 201, secret: { token: 'data.token' } }
    await assert.rejects(run(root, demo([issue, { capture: 'demo-token', method: 'POST', path: '/token', expect: 201 }])), (error) => {
      assert.match(error.message, /demo-token.*secret/)
      assert.ok(!error.message.includes(FIXTURE_TOKEN))
      return true
    })
    const use = { capture: 'demo-token', method: 'GET', path: '/list', bearer: '{{token}}', expect: 200 }
    const ok = await run(root, demo([issue, use]))
    assert.ok(!ok.blocks.get('demo-token').includes(FIXTURE_TOKEN))
    assert.equal(ok.stub.requests[1].headers.authorization, `Bearer ${FIXTURE_TOKEN}`)
  } finally {
    cleanup(root)
  }
})

test('print.mask replaces a registered secret in the whole block text; an undeclared secret is still refused', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-token')) })
  try {
    const issue = {
      capture: 'demo-token',
      method: 'POST',
      path: '/token',
      expect: 201,
      secret: { token: 'data.token' },
      print: { mask: { token: '<token>' } },
    }
    const masked = await run(root, demo([issue]), { write: true })
    assert.ok(masked.blocks.get('demo-token').includes('"token":"<token>"'))
    assert.ok(!read(root, 'en').includes(FIXTURE_TOKEN))
    await assert.rejects(run(root, demo([{ ...issue, print: {} }])), /demo-token.*secret.*mask/)
    await assert.rejects(run(root, demo([{ ...issue, print: { mask: { nobody: '<x>' } } }])), /demo-token.*'nobody'.*no step registered/)
  } finally {
    cleanup(root)
  }
})

test('the admin password can be masked by name', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-leak')) })
  try {
    const step = {
      capture: 'demo-leak',
      method: 'POST',
      path: '/created',
      body: (vars) => ({ phrase: vars.adminPassword }),
      print: { mask: { adminPassword: '<your-admin-password>' } },
      expect: 201,
    }
    const result = await run(root, demo([step]))
    assert.ok(result.blocks.get('demo-leak').includes('<your-admin-password>'))
  } finally {
    cleanup(root)
  }
})

test('a session cookie is a secret as soon as it is set: printing headers needs a declared mask', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-login')) })
  try {
    const login = { capture: 'demo-login', as: 'visitor', method: 'POST', path: '/login', body: {}, expect: 200, print: { headers: true } }
    await assert.rejects(run(root, demo([login])), /demo-login.*secret.*mask/)
    const masked = { ...login, print: { headers: true, mask: { session_visitor: '<session>' } } }
    const ok = await run(root, demo([masked]))
    assert.ok(ok.blocks.get('demo-login').includes('Set-Cookie: sess=<session>; path=/'))
    assert.ok(!ok.blocks.get('demo-login').includes(FIXTURE_COOKIE))
  } finally {
    cleanup(root)
  }
})

test('declared transforms apply to the printed body only, and an undeclared difference does not exist', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-bad')) })
  try {
    const step = { capture: 'demo-bad', method: 'GET', path: '/bad', expect: 400 }
    const stripped = await run(root, demo([{ ...step, transforms: ['stripTraceId'] }]))
    assert.equal(stripped.blocks.get('demo-bad'), '$ GET /bad\n{"error":{"code":"X"}}\nHTTP_STATUS:400')
    assert.ok((await run(root, demo([step]))).blocks.get('demo-bad').includes('traceId'))
  } finally {
    cleanup(root)
  }
})

test('a failing transform names the capture, the file and the request, not a bare transform message', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-list')) })
  try {
    await assert.rejects(run(root, demo([{ ...list, transforms: ['stripTraceId'] }])), (error) => {
      assert.ok(error instanceof CaptureError)
      assert.match(error.message, /demo-list.*01-demo\.md.*GET \/list.*stripTraceId: the body has no error object/)
      assert.equal(error.file, DEMO)
      return true
    })
  } finally {
    cleanup(root)
  }
})

test('steps sharing a capture id become one block, exchanges separated by a blank line', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-two')) })
  try {
    const steps = [
      { capture: 'demo-two', method: 'GET', path: '/list', expect: 200 },
      { capture: 'demo-two', method: 'GET', path: '/bad', expect: 400 },
    ]
    assert.equal((await run(root, demo(steps))).blocks.get('demo-two').split('\n\n').length, 2)
  } finally {
    cleanup(root)
  }
})

test('a status other than the declared one fails, naming the capture id and the status', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-list')) })
  try {
    await assert.rejects(run(root, demo([{ ...list, expect: 201 }])), /demo-list.*expected HTTP 201.*got 200/)
  } finally {
    cleanup(root)
  }
})

test('a step without a declared status is rejected before any request is sent', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-list')) })
  try {
    await assert.rejects(
      run(root, demo([{ capture: 'demo-list', method: 'GET', path: '/list' }])),
      (error) => /demo-list.*declares no expected status/.test(error.message) && error.stub.requests.length === 0,
    )
  } finally {
    cleanup(root)
  }
})

test('a marker with no capture, or a capture with no marker, fails naming id and file before any request', async () => {
  const root = makeGuide({ [DEMO]: { zh: BLOCK('demo-list') + BLOCK('orphan'), en: BLOCK('demo-list') + BLOCK('orphan') } })
  try {
    await assert.rejects(run(root, demo([list])), (error) => {
      assert.match(error.message, /orphan.*zh-TW[\\/]01-demo\.md.*no capture/)
      assert.equal(error.stub.requests.length, 0)
      return true
    })
    writeFileSync(join(root, 'en', DEMO), '# nothing\n')
    await assert.rejects(run(root, demo([list])), /demo-list.*no marker.*en[\\/]01-demo\.md/)
  } finally {
    cleanup(root)
  }
})

test('blocks that would differ between the languages fail and nothing is written', async () => {
  const zh = BLOCK('demo-list', 'old', '```text')
  const root = makeGuide({ [DEMO]: { zh, en: BLOCK('demo-list', 'old', '```json') } })
  try {
    await assert.rejects(run(root, demo([list]), { write: true }), /demo-list.*differ/)
    assert.equal(read(root, 'zh-TW'), zh)
  } finally {
    cleanup(root)
  }
})

test('one file is fed by two groups that are not adjacent, in run order, with shared state', async () => {
  const root = makeGuide({
    [DEMO]: same(BLOCK('one-first') + BLOCK('one-third')),
    [OTHER]: same(BLOCK('two-second')),
  })
  try {
    const groups = [
      {
        name: 'a',
        file: DEMO,
        steps: [created, { capture: 'one-first', method: 'GET', path: '/item/{{itemId}}?n=1', expect: 200 }],
      },
      { name: 'b', file: OTHER, steps: [{ capture: 'two-second', method: 'GET', path: '/item/{{itemId}}?n=2', expect: 200 }] },
      { name: 'c', file: DEMO, steps: [{ capture: 'one-third', method: 'GET', path: '/item/{{itemId}}?n=3', expect: 200 }] },
    ]
    const result = await run(root, groups, { write: true })
    assert.deepEqual(
      result.stub.requests.map((r) => r.path),
      ['/created', '/item/abc-1?n=1', '/item/abc-1?n=2', '/item/abc-1?n=3'],
    )
    const one = read(root, 'en')
    assert.ok(one.indexOf('?n=1') < one.indexOf('?n=3') && one.includes('one-third'))
    assert.ok(read(root, 'zh-TW', OTHER).includes('?n=2'))
    assert.equal(read(root, 'en'), read(root, 'zh-TW'))
  } finally {
    cleanup(root)
  }
})

test('only chooses which files are written; every group still runs, and unselected files are not read', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('one')) })
  try {
    const groups = [
      { name: 'a', file: DEMO, steps: [{ capture: 'one', method: 'GET', path: '/list', expect: 200 }] },
      { name: 'b', file: '02-missing.md', steps: [{ capture: 'two', method: 'GET', path: '/created', expect: 201 }] },
    ]
    const result = await run(root, groups, { write: true, only: [DEMO] })
    assert.equal(result.stub.requests.length, 2)
    assert.deepEqual(result.files.map((file) => file.label), ['zh-TW/01-demo.md', 'en/01-demo.md'])
    assert.ok(read(root, 'en').includes(STATUS_OK))
  } finally {
    cleanup(root)
  }
})

test('a failure in a later file leaves every chapter file unchanged and no temporary file behind', async () => {
  const text = BLOCK('one')
  const root = makeGuide({ [DEMO]: same(text), [OTHER]: same(BLOCK('two')) })
  try {
    const groups = [
      { name: 'a', file: DEMO, steps: [{ capture: 'one', method: 'GET', path: '/list', expect: 200 }] },
      { name: 'b', file: OTHER, steps: [{ capture: 'two', method: 'GET', path: '/list', expect: 400 }] },
    ]
    await assert.rejects(run(root, groups, { write: true }), /two.*expected HTTP 400/)
    assert.equal(read(root, 'en'), text)
    assert.deepEqual(readdirSync(join(root, 'en')).sort((a, b) => a.localeCompare(b)), [DEMO, OTHER])
  } finally {
    cleanup(root)
  }
})

test('a failure while writing the temporary files is a CaptureError, changes no chapter file and cleans up', async () => {
  const text = BLOCK('one')
  const root = makeGuide({ [DEMO]: same(text) })
  let writes = 0
  const io = {
    writeFileSync: (path, content) => {
      writes += 1
      if (writes === 2) throw new Error('disk full')
      writeFileSync(path, content)
    },
    renameSync: (from, to) => {
      throw new Error(`rename must not run: ${from} -> ${to}`)
    },
    rmSync: (path) => rmSync(path, { force: true }),
  }
  try {
    await assert.rejects(run(root, demo([{ ...list, capture: 'one' }]), { write: true, io }), (error) => {
      assert.ok(error instanceof CaptureError)
      assert.match(error.message, /disk full.*no chapter file was changed/)
      return true
    })
    assert.equal(read(root, 'zh-TW'), text)
    assert.deepEqual(readdirSync(join(root, 'zh-TW')), [DEMO])
  } finally {
    cleanup(root)
  }
})

test('a rename that fails half-way removes the temporary files and says which files were replaced', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('one')), [OTHER]: same(BLOCK('two')) })
  let renames = 0
  const io = {
    writeFileSync,
    renameSync: (from, to) => {
      renames += 1
      if (renames === 3) throw new Error('access denied')
      renameSync(from, to)
    },
    rmSync: (path) => rmSync(path, { force: true }),
  }
  try {
    const groups = [
      { name: 'a', file: DEMO, steps: [{ capture: 'one', method: 'GET', path: '/list', expect: 200 }] },
      { name: 'b', file: OTHER, steps: [{ capture: 'two', method: 'GET', path: '/list', expect: 200 }] },
    ]
    await assert.rejects(run(root, groups, { write: true, io }), (error) => {
      assert.ok(error instanceof CaptureError)
      assert.match(error.message, /access denied/)
      assert.match(error.message, /already replaced: zh-TW\/01-demo\.md, en\/01-demo\.md/)
      assert.match(error.message, /not replaced: zh-TW\/02-other\.md, en\/02-other\.md/)
      assert.match(error.message, /two languages of a file are replaced one after the other/)
      return true
    })
    assert.ok(read(root, 'en').includes(STATUS_OK))
    assert.ok(read(root, 'zh-TW', OTHER).includes('old'))
    for (const dir of ['zh-TW', 'en']) assert.ok(!readdirSync(join(root, dir)).some((name) => name.endsWith('.capture-tmp')))
  } finally {
    cleanup(root)
  }
})

test('a chapter file that cannot be read is a CaptureError naming it', async () => {
  const root = makeGuide({})
  try {
    await assert.rejects(run(root, [{ name: 'a', file: '09-none.md', steps: [{ capture: 'x', method: 'GET', path: '/list', expect: 200 }] }]), (error) => {
      assert.ok(error instanceof CaptureError)
      assert.match(error.message, /09-none\.md/)
      return true
    })
  } finally {
    cleanup(root)
  }
})

test('a response cut off mid-body fails the step with a CaptureError that names it', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-list')) })
  try {
    await assert.rejects(run(root, demo([{ ...list, path: '/cut' }])), (error) => {
      assert.ok(error instanceof CaptureError)
      assert.match(error.message, /demo-list.*GET \/cut.*cut off before its body was complete/)
      return true
    })
  } finally {
    cleanup(root)
  }
})

test('a DELETE is accepted when a whole path segment is a value from a 201 response of this run', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-del')) })
  try {
    const del = { capture: 'demo-del', method: 'DELETE', path: '/item/{{itemId}}', expect: 200 }
    const ok = await run(root, demo([created, del]))
    assert.equal(ok.stub.requests[1].method, 'DELETE')
    const withQuery = await run(root, demo([created, { ...del, path: '/item/{{itemId}}?purge=1' }]))
    assert.equal(withQuery.stub.requests[1].path, '/item/abc-1?purge=1')
    const suffix = { ...del, path: '/item/{{itemId}}-extra' }
    await assert.rejects(
      run(root, demo([created, suffix])),
      (error) => /demo-del.*DELETE refused/.test(error.message) && error.stub.requests.length === 1,
    )
    await assert.rejects(run(root, demo([{ ...del, path: '/item/literal-id' }])), /DELETE refused/)
  } finally {
    cleanup(root)
  }
})

test('a value saved by a read is not a row the run created', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-del')) })
  try {
    const read9 = { method: 'GET', path: '/read', expect: 200, save: { rid: 'data.id' } }
    const del = { capture: 'demo-del', method: 'DELETE', path: '/item/{{rid}}', expect: 200 }
    await assert.rejects(run(root, demo([read9, del])), /demo-del.*DELETE refused/)
  } finally {
    cleanup(root)
  }
})

test('a DELETE of something the run did not create needs a declared reason on the step', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-del')) })
  try {
    const del = { capture: 'demo-del', method: 'DELETE', path: '/item/literal-id', expect: 200 }
    const ok = await run(root, demo([{ ...del, deletes: 'revokes the token this run issued' }]))
    assert.equal(ok.stub.requests[0].method, 'DELETE')
    await assert.rejects(run(root, demo([{ ...del, deletes: '' }])), /DELETE refused/)
  } finally {
    cleanup(root)
  }
})

test('captured text with a carriage return is refused with its capture id', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-cr')) })
  try {
    await assert.rejects(run(root, demo([{ capture: 'demo-cr', method: 'GET', path: '/crlf', expect: 200 }])), /demo-cr.*carriage return/)
  } finally {
    cleanup(root)
  }
})

test('the guard runs before any step, and its failure stops the run with nothing written', async () => {
  const text = BLOCK('demo-list')
  const root = makeGuide({ [DEMO]: same(text) })
  try {
    const guard = async () => {
      throw new CaptureError('not fresh')
    }
    await assert.rejects(run(root, demo([list]), { guard, write: true }), (error) => /not fresh/.test(error.message) && error.stub.requests.length === 0)
    assert.equal(read(root, 'en'), text)
    const seen = async ({ client }) => {
      await client.request({ method: 'GET', path: '/guard-probe' })
    }
    const result = await run(root, demo([list]), { guard: seen })
    assert.deepEqual(result.stub.requests.map((r) => r.path), ['/guard-probe', '/list'])
  } finally {
    cleanup(root)
  }
})

test('an unreachable host and a request that gets no answer both name the step', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-list')) })
  try {
    const stub = await startStub(api)
    const baseUrl = stub.baseUrl
    await stub.close()
    await assert.rejects(
      runCapture({ baseUrl, vars: {}, groups: demo([list]), guideDir: root, write: false, guard: noGuard }),
      /demo-list.*GET \/list.*unreachable at http:\/\/127\.0\.0\.1:\d+/,
    )
    await assert.rejects(run(root, demo([{ ...list, timeoutMs: 100 }]), {}, () => ({ hang: true })), /demo-list.*GET \/list.*no answer within 100 ms/)
  } finally {
    cleanup(root)
  }
})

test('one capture id in two files is refused before any request', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('dup')), [OTHER]: same(BLOCK('dup')) })
  try {
    const groups = [
      { name: 'a', file: DEMO, steps: [{ capture: 'dup', method: 'GET', path: '/list', expect: 200 }] },
      { name: 'b', file: OTHER, steps: [{ capture: 'dup', method: 'GET', path: '/list', expect: 200 }] },
    ]
    await assert.rejects(run(root, groups), (error) => /dup.*01-demo\.md and 02-other\.md/.test(error.message) && error.stub.requests.length === 0)
  } finally {
    cleanup(root)
  }
})

test('a verify hook that throws stops the run with its message', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-list')) })
  try {
    const verify = (body) => {
      if (body.meta.total > 0) throw new CaptureError('database is not fresh')
    }
    await assert.rejects(run(root, demo([{ method: 'GET', path: '/list', expect: 200, verify }, list])), /database is not fresh/)
  } finally {
    cleanup(root)
  }
})

test('only a saved id counts as a created row: a version or a name saved from a 201 does not open a DELETE', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-del')) })
  try {
    const handler = (request) => (request.path === '/made' ? json(201, { id: 'abc-1', version: '0', kind: 'item' }) : api(request))
    const made = { method: 'POST', path: '/made', expect: 201, save: { itemId: 'data.id', version: 'data.version', kind: 'data.kind' } }
    const del = { capture: 'demo-del', method: 'DELETE', expect: 200 }
    await assert.rejects(run(root, demo([made, { ...del, path: '/item/{{version}}' }]), {}, handler), /demo-del.*DELETE refused/)
    await assert.rejects(run(root, demo([made, { ...del, path: '/{{kind}}/zzz' }]), {}, handler), /demo-del.*DELETE refused/)
    const ok = await run(root, demo([made, { ...del, path: '/item/{{itemId}}' }]), {}, handler)
    assert.equal(ok.stub.requests[1].method, 'DELETE')
  } finally {
    cleanup(root)
  }
})

test('a declared delete reason of only whitespace is no reason', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-del')) })
  try {
    const del = { capture: 'demo-del', method: 'DELETE', path: '/item/literal-id', expect: 200, deletes: '   ' }
    await assert.rejects(run(root, demo([del])), /DELETE refused/)
  } finally {
    cleanup(root)
  }
})

test('masking a secret that sits inside a longer secret does not print the rest of the longer one', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-nested')) })
  try {
    const outer = `xx${FIXTURE_TOKEN}yy`
    const handler = (request) => (request.path === '/nested' ? json(201, { token: FIXTURE_TOKEN, outer }) : api(request))
    const step = {
      capture: 'demo-nested',
      method: 'POST',
      path: '/nested',
      expect: 201,
      secret: { token: 'data.token', outer: 'data.outer' },
    }
    await assert.rejects(
      run(root, demo([{ ...step, print: { mask: { token: '<token>' } } }]), {}, handler),
      (error) => /demo-nested.*secret.*mask/.test(error.message) && !error.message.includes(FIXTURE_TOKEN),
    )
    const both = await run(root, demo([{ ...step, print: { mask: { token: '<token>', outer: '<outer>' } } }]), {}, handler)
    const text = both.blocks.get('demo-nested')
    assert.ok(text.includes('"token":"<token>"') && text.includes('"outer":"<outer>"'), text)
  } finally {
    cleanup(root)
  }
})

test('the cookie the guard receives is a secret too, and can be masked by its identity name', async () => {
  const root = makeGuide({ [DEMO]: same(BLOCK('demo-echo')) })
  try {
    const handler = (request) =>
      request.path === '/echo-cookie' ? { status: 200, body: JSON.stringify({ seen: FIXTURE_COOKIE }) } : api(request)
    const guard = async ({ client }) => {
      await client.request({ identity: 'admin', method: 'POST', path: '/login' })
    }
    const echo = { capture: 'demo-echo', as: 'admin', method: 'GET', path: '/echo-cookie', expect: 200 }
    await assert.rejects(run(root, demo([echo]), { guard }, handler), /demo-echo.*secret.*mask/)
    const masked = await run(root, demo([{ ...echo, print: { mask: { session_admin: '<cookie>' } } }]), { guard }, handler)
    assert.ok(masked.blocks.get('demo-echo').includes('"seen":"<cookie>"'))
  } finally {
    cleanup(root)
  }
})
