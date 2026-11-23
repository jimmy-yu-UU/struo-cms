import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { test } from 'node:test'
import { startStub } from '../test-support/stub-server.mjs'
import { CaptureError, runCapture } from './runner.mjs'

const BLOCK = (id, body = 'old', fence = '```text') => `<!-- capture: ${id} -->\n${fence}\n${body}\n\`\`\`\n`
const noGuard = async () => {}

// files: { '01-demo.md': { zh, en } }
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
const read = (root, dir, name = '01-demo.md') => readFileSync(join(root, dir, name), 'utf8')

const api = (request) => {
  if (request.path === '/list') return { status: 200, body: '{"success":true,"data":[1],"meta":{"total":1}}' }
  if (request.path === '/created') return { status: 201, body: '{"success":true,"data":{"id":"abc-1"}}' }
  if (request.path === '/token') return { status: 201, body: '{"success":true,"data":{"token":"tok-SECRET-123"}}' }
  if (request.path === '/echo-password') return { status: 200, body: '{"echo":"Passw0rd-xyz"}' }
  if (request.path.startsWith('/item/abc-1')) return { status: 200, body: '{"success":true,"data":"got"}' }
  if (request.path === '/bad') return { status: 400, body: `{"error":{"code":"X","traceId":"${'f'.repeat(32)}"}}` }
  if (request.path === '/crlf') return { status: 200, body: 'a\r\nb' }
  return { status: 200, body: '{"ok":true}' }
}

async function run(root, groups, over = {}, handler = api) {
  const stub = await startStub(handler)
  try {
    const result = await runCapture({
      baseUrl: stub.baseUrl,
      vars: { adminPassword: 'Passw0rd-xyz' },
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

const demo = (steps) => [{ name: 'demo', file: '01-demo.md', steps }]
const list = { capture: 'demo-list', method: 'GET', path: '/list', expect: 200 }
const cleanup = (root) => rmSync(root, { recursive: true, force: true })

test('captures are written into both languages with the same text', async () => {
  const root = makeGuide({ '01-demo.md': same(`# 一\n\n${BLOCK('demo-list')}`) })
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
  const root = makeGuide({ '01-demo.md': same(text) })
  try {
    await run(root, demo([list]))
    assert.equal(read(root, 'en'), text)
  } finally {
    cleanup(root)
  }
})

test('{{name}} takes ids from earlier responses into paths, bodies and the printed line; single braces are text', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-get') + BLOCK('demo-gql')) })
  try {
    const steps = [
      { method: 'POST', path: '/created', body: { name: 'n' }, expect: 201, save: { itemId: 'data.id' } },
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
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-list')) })
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

test('a request body object is sent compact and printed exactly as sent', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-post')) })
  try {
    const result = await run(root, demo([{ capture: 'demo-post', method: 'POST', path: '/created', body: { a: 1, b: ['x'] }, expect: 201 }]))
    assert.ok(result.blocks.get('demo-post').includes('body:\n{"a":1,"b":["x"]}\n'))
    assert.equal(result.stub.requests[0].body, '{"a":1,"b":["x"]}')
  } finally {
    cleanup(root)
  }
})

test('a declared request-print override changes the printed body only', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-login')) })
  try {
    const step = {
      capture: 'demo-login',
      method: 'POST',
      path: '/created',
      body: (vars) => ({ password: vars.adminPassword }),
      print: { requestBody: '{"password":"<your-admin-password>"}' },
      expect: 201,
    }
    const result = await run(root, demo([step]))
    assert.ok(result.blocks.get('demo-login').includes('body:\n{"password":"<your-admin-password>"}\n'))
    assert.equal(result.stub.requests[0].body, '{"password":"Passw0rd-xyz"}')
  } finally {
    cleanup(root)
  }
})

test('a block that holds the admin password is refused with its capture id, and the password is never echoed', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-leak')) })
  try {
    const leak = { capture: 'demo-leak', method: 'POST', path: '/created', body: (vars) => ({ password: vars.adminPassword }), expect: 201 }
    await assert.rejects(run(root, demo([leak]), { write: true }), (error) => {
      assert.match(error.message, /demo-leak.*secret/)
      assert.ok(!error.message.includes('Passw0rd-xyz'))
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

test('a value a step registers as secret is refused in any later block', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-token')) })
  try {
    const steps = [
      { method: 'POST', path: '/token', expect: 201, secret: { token: 'data.token' } },
      { capture: 'demo-token', method: 'POST', path: '/token', expect: 201 },
    ]
    await assert.rejects(run(root, demo(steps)), (error) => {
      assert.match(error.message, /demo-token.*secret/)
      assert.ok(!error.message.includes('tok-SECRET-123'))
      return true
    })
    const printed = [steps[0], { ...steps[1], print: {}, transforms: [] }]
    printed[1] = { capture: 'demo-token', method: 'GET', path: '/list', bearer: '{{token}}', expect: 200 }
    const ok = await run(root, demo(printed))
    assert.ok(!ok.blocks.get('demo-token').includes('tok-SECRET-123'))
    assert.equal(ok.stub.requests[1].headers.authorization, 'Bearer tok-SECRET-123')
  } finally {
    cleanup(root)
  }
})

test('declared transforms apply to the printed body only, and an undeclared difference does not exist', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-bad')) })
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
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-list')) })
  try {
    await assert.rejects(run(root, demo([{ ...list, transforms: ['stripTraceId'] }])), (error) => {
      assert.ok(error instanceof CaptureError)
      assert.match(error.message, /demo-list.*01-demo\.md.*GET \/list.*stripTraceId: the body has no error object/)
      assert.equal(error.file, '01-demo.md')
      return true
    })
  } finally {
    cleanup(root)
  }
})

test('steps sharing a capture id become one block, exchanges separated by a blank line', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-two')) })
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
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-list')) })
  try {
    await assert.rejects(run(root, demo([{ ...list, expect: 201 }])), /demo-list.*expected HTTP 201.*got 200/)
  } finally {
    cleanup(root)
  }
})

test('a step without a declared status is rejected before any request is sent', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-list')) })
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
  const root = makeGuide({ '01-demo.md': { zh: BLOCK('demo-list') + BLOCK('orphan'), en: BLOCK('demo-list') + BLOCK('orphan') } })
  try {
    await assert.rejects(run(root, demo([list])), (error) => {
      assert.match(error.message, /orphan.*zh-TW[\\/]01-demo\.md.*no capture/)
      assert.equal(error.stub.requests.length, 0)
      return true
    })
    writeFileSync(join(root, 'en', '01-demo.md'), '# nothing\n')
    await assert.rejects(run(root, demo([list])), /demo-list.*no marker.*en[\\/]01-demo\.md/)
  } finally {
    cleanup(root)
  }
})

test('blocks that would differ between the languages fail and nothing is written', async () => {
  const zh = BLOCK('demo-list', 'old', '```text')
  const root = makeGuide({ '01-demo.md': { zh, en: BLOCK('demo-list', 'old', '```json') } })
  try {
    await assert.rejects(run(root, demo([list]), { write: true }), /demo-list.*differ/)
    assert.equal(read(root, 'zh-TW'), zh)
  } finally {
    cleanup(root)
  }
})

test('one file is fed by two groups that are not adjacent, in run order, with shared state', async () => {
  const root = makeGuide({
    '01-demo.md': same(BLOCK('one-first') + BLOCK('one-third')),
    '02-other.md': same(BLOCK('two-second')),
  })
  try {
    const groups = [
      {
        name: 'a',
        file: '01-demo.md',
        steps: [
          { method: 'POST', path: '/created', expect: 201, save: { itemId: 'data.id' } },
          { capture: 'one-first', method: 'GET', path: '/item/{{itemId}}?n=1', expect: 200 },
        ],
      },
      { name: 'b', file: '02-other.md', steps: [{ capture: 'two-second', method: 'GET', path: '/item/{{itemId}}?n=2', expect: 200 }] },
      { name: 'c', file: '01-demo.md', steps: [{ capture: 'one-third', method: 'GET', path: '/item/{{itemId}}?n=3', expect: 200 }] },
    ]
    const result = await run(root, groups, { write: true })
    assert.deepEqual(
      result.stub.requests.map((r) => r.path),
      ['/created', '/item/abc-1?n=1', '/item/abc-1?n=2', '/item/abc-1?n=3'],
    )
    const one = read(root, 'en')
    assert.ok(one.indexOf('?n=1') < one.indexOf('?n=3') && one.includes('one-third'))
    assert.ok(read(root, 'zh-TW', '02-other.md').includes('?n=2'))
    assert.equal(read(root, 'en'), read(root, 'zh-TW'))
  } finally {
    cleanup(root)
  }
})

test('only chooses which files are written; every group still runs, and unselected files are not read', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('one')) })
  try {
    const groups = [
      { name: 'a', file: '01-demo.md', steps: [{ capture: 'one', method: 'GET', path: '/list', expect: 200 }] },
      { name: 'b', file: '02-missing.md', steps: [{ capture: 'two', method: 'GET', path: '/created', expect: 201 }] },
    ]
    const result = await run(root, groups, { write: true, only: ['01-demo.md'] })
    assert.equal(result.stub.requests.length, 2)
    assert.deepEqual(
      result.files.map((file) => file.path.slice(root.length + 1).replaceAll('\\', '/')),
      ['zh-TW/01-demo.md', 'en/01-demo.md'],
    )
    assert.ok(read(root, 'en').includes('HTTP_STATUS:200'))
  } finally {
    cleanup(root)
  }
})

test('a failure in a later file leaves every chapter file unchanged and no temporary file behind', async () => {
  const text = BLOCK('one')
  const root = makeGuide({ '01-demo.md': same(text), '02-other.md': same(BLOCK('two')) })
  try {
    const groups = [
      { name: 'a', file: '01-demo.md', steps: [{ capture: 'one', method: 'GET', path: '/list', expect: 200 }] },
      { name: 'b', file: '02-other.md', steps: [{ capture: 'two', method: 'GET', path: '/list', expect: 400 }] },
    ]
    await assert.rejects(run(root, groups, { write: true }), /two.*expected HTTP 400/)
    assert.equal(read(root, 'en'), text)
    assert.deepEqual(readdirSync(join(root, 'en')).sort(), ['01-demo.md', '02-other.md'])
  } finally {
    cleanup(root)
  }
})

test('a failure while writing the temporary files leaves every chapter file unchanged and cleans up', async () => {
  const text = BLOCK('one')
  const root = makeGuide({ '01-demo.md': same(text) })
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
    await assert.rejects(run(root, demo([{ ...list, capture: 'one' }]), { write: true, io }), /disk full/)
    assert.equal(read(root, 'zh-TW'), text)
    assert.deepEqual(readdirSync(join(root, 'zh-TW')), ['01-demo.md'])
  } finally {
    cleanup(root)
  }
})

test('a DELETE must target a value the run saved from a response', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-del')) })
  try {
    const del = { capture: 'demo-del', method: 'DELETE', path: '/item/{{itemId}}', expect: 200 }
    const created = { method: 'POST', path: '/created', expect: 201, save: { itemId: 'data.id' } }
    const ok = await run(root, demo([created, del]))
    assert.equal(ok.stub.requests[1].method, 'DELETE')
    await assert.rejects(
      run(root, demo([{ ...del, path: '/item/literal-id' }])),
      (error) => /demo-del.*DELETE.*no value this run saved/.test(error.message) && error.stub.requests.length === 0,
    )
  } finally {
    cleanup(root)
  }
})

test('captured text with a carriage return is refused with its capture id', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-cr')) })
  try {
    await assert.rejects(run(root, demo([{ capture: 'demo-cr', method: 'GET', path: '/crlf', expect: 200 }])), /demo-cr.*carriage return/)
  } finally {
    cleanup(root)
  }
})

test('the guard runs before any step, and its failure stops the run with nothing written', async () => {
  const text = BLOCK('demo-list')
  const root = makeGuide({ '01-demo.md': same(text) })
  try {
    const guard = async () => {
      throw new CaptureError('not fresh')
    }
    await assert.rejects(run(root, demo([list]), { guard, write: true }), (error) => /not fresh/.test(error.message) && error.stub.requests.length === 0)
    assert.equal(read(root, 'en'), text)
    const order = []
    const seen = async ({ client }) => {
      order.push('guard')
      await client.request({ method: 'GET', path: '/guard-probe' })
    }
    const result = await run(root, demo([list]), { guard: seen })
    assert.deepEqual(result.stub.requests.map((r) => r.path), ['/guard-probe', '/list'])
  } finally {
    cleanup(root)
  }
})

test('an unreachable host and a request that gets no answer both name the step', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-list')) })
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
  const root = makeGuide({ '01-demo.md': same(BLOCK('dup')), '02-other.md': same(BLOCK('dup')) })
  try {
    const groups = [
      { name: 'a', file: '01-demo.md', steps: [{ capture: 'dup', method: 'GET', path: '/list', expect: 200 }] },
      { name: 'b', file: '02-other.md', steps: [{ capture: 'dup', method: 'GET', path: '/list', expect: 200 }] },
    ]
    await assert.rejects(run(root, groups), (error) => /dup.*01-demo\.md and 02-other\.md/.test(error.message) && error.stub.requests.length === 0)
  } finally {
    cleanup(root)
  }
})

test('a verify hook that throws stops the run with its message', async () => {
  const root = makeGuide({ '01-demo.md': same(BLOCK('demo-list')) })
  try {
    const verify = (json) => {
      if (json.meta.total > 0) throw new CaptureError('database is not fresh')
    }
    await assert.rejects(run(root, demo([{ method: 'GET', path: '/list', expect: 200, verify }, list])), /database is not fresh/)
  } finally {
    cleanup(root)
  }
})
