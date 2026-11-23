import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { test } from 'node:test'
import { CaptureError, runCapture } from './runner.mjs'
import { startStub } from './stub-server.mjs'

const BLOCK = (id, body = 'old', fence = '```text') => `<!-- capture: ${id} -->\n${fence}\n${body}\n\`\`\`\n`

function makeGuide({ zh, en, name = '01-demo.md' }) {
  const root = mkdtempSync(join(tmpdir(), 'struo-capture-'))
  for (const [dir, text] of [['zh-TW', zh], ['en', en]]) {
    mkdirSync(join(root, dir))
    writeFileSync(join(root, dir, name), text)
  }
  return root
}

const chapter = (steps) => ({ number: '01', file: '01-demo.md', steps })
const SEED = []

const api = (request) => {
  if (request.path === '/list') return { status: 200, body: '{"success":true,"data":[1],"meta":{"total":1}}' }
  if (request.path === '/created') return { status: 201, body: '{"success":true,"data":{"id":"abc-1"}}' }
  if (request.path.startsWith('/item/abc-1')) return { status: 200, body: '{"success":true,"data":"got"}' }
  if (request.path === '/bad') return { status: 400, body: `{"error":{"code":"X","traceId":"${'f'.repeat(32)}"}}` }
  return { status: 404, body: 'no' }
}

async function run(guideRoot, steps, over = {}, handler = api) {
  const stub = await startStub(handler)
  try {
    const result = await runCapture({
      baseUrl: stub.baseUrl,
      vars: {},
      seed: SEED,
      chapters: [chapter(steps)],
      guideDir: guideRoot,
      write: false,
      ...over,
    })
    return { ...result, stub }
  } finally {
    await stub.close()
  }
}

const list = { capture: 'demo-list', method: 'GET', path: '/list', expect: 200 }

test('captures are written into both languages with the same text', async () => {
  const root = makeGuide({ zh: `# 一\n\n${BLOCK('demo-list')}`, en: `# One\n\n${BLOCK('demo-list')}` })
  try {
    const result = await run(root, [list], { write: true })
    const expected = '$ GET /list\n{"success":true,"data":[1],"meta":{"total":1}}\nHTTP_STATUS:200'
    for (const dir of ['zh-TW', 'en']) {
      assert.ok(readFileSync(join(root, dir, '01-demo.md'), 'utf8').includes(`\`\`\`text\n${expected}\n\`\`\``))
    }
    assert.equal(result.blocks.get('demo-list'), expected)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('without write nothing on disk changes', async () => {
  const text = `${BLOCK('demo-list')}`
  const root = makeGuide({ zh: text, en: text })
  try {
    await run(root, [list], { write: false })
    assert.equal(readFileSync(join(root, 'en', '01-demo.md'), 'utf8'), text)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('ids from earlier responses are substituted into paths, bodies and the printed request line', async () => {
  const text = BLOCK('demo-get')
  const root = makeGuide({ zh: text, en: text })
  try {
    const steps = [
      { method: 'POST', path: '/created', body: { name: 'n' }, expect: 201, save: { itemId: 'data.id' } },
      { capture: 'demo-get', method: 'GET', path: '/item/{itemId}?x=1', expect: 200 },
    ]
    const result = await run(root, steps)
    assert.ok(result.blocks.get('demo-get').startsWith('$ GET /item/abc-1?x=1\n'))
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('a request body object is sent compact and printed exactly as sent', async () => {
  const text = BLOCK('demo-post')
  const root = makeGuide({ zh: text, en: text })
  try {
    const steps = [{ capture: 'demo-post', method: 'POST', path: '/created', body: { a: 1, b: ['x'] }, expect: 201 }]
    const result = await run(root, steps)
    assert.ok(result.blocks.get('demo-post').includes('body:\n{"a":1,"b":["x"]}\n'))
    assert.equal(result.stub.requests[0].body, '{"a":1,"b":["x"]}')
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('declared transforms apply to the printed body only', async () => {
  const text = BLOCK('demo-bad')
  const root = makeGuide({ zh: text, en: text })
  try {
    const steps = [{ capture: 'demo-bad', method: 'GET', path: '/bad', expect: 400, transforms: ['stripTraceId'] }]
    const result = await run(root, steps)
    assert.equal(result.blocks.get('demo-bad'), '$ GET /bad\n{"error":{"code":"X"}}\nHTTP_STATUS:400')
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('without a declared transform the traceId is printed as received', async () => {
  const text = BLOCK('demo-bad')
  const root = makeGuide({ zh: text, en: text })
  try {
    const result = await run(root, [{ capture: 'demo-bad', method: 'GET', path: '/bad', expect: 400 }])
    assert.ok(result.blocks.get('demo-bad').includes('traceId'))
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('steps sharing a capture id become one block, exchanges separated by a blank line', async () => {
  const text = BLOCK('demo-two')
  const root = makeGuide({ zh: text, en: text })
  try {
    const steps = [
      { capture: 'demo-two', method: 'GET', path: '/list', expect: 200 },
      { capture: 'demo-two', method: 'GET', path: '/bad', expect: 400 },
    ]
    const result = await run(root, steps)
    assert.equal(result.blocks.get('demo-two').split('\n\n').length, 2)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('a status other than the declared one fails, naming the capture id and the status', async () => {
  const text = BLOCK('demo-list')
  const root = makeGuide({ zh: text, en: text })
  try {
    await assert.rejects(run(root, [{ ...list, expect: 201 }]), (error) => {
      assert.ok(error instanceof CaptureError)
      assert.match(error.message, /demo-list.*expected HTTP 201.*got 200/)
      return true
    })
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('a step without a declared status is rejected before any request is sent', async () => {
  const text = BLOCK('demo-list')
  const root = makeGuide({ zh: text, en: text })
  try {
    await assert.rejects(run(root, [{ capture: 'demo-list', method: 'GET', path: '/list' }]), /demo-list.*declares no expected status/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('a variable that no earlier response saved is an error', async () => {
  const text = BLOCK('demo-list')
  const root = makeGuide({ zh: text, en: text })
  try {
    await assert.rejects(run(root, [{ ...list, path: '/item/{nope}' }]), /unknown variable 'nope'/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('a marker with no capture fails in both languages, naming id and file, before any request', async () => {
  const root = makeGuide({ zh: BLOCK('demo-list') + BLOCK('orphan'), en: BLOCK('demo-list') })
  try {
    const stub = await startStub(api)
    await assert.rejects(
      runCapture({ baseUrl: stub.baseUrl, vars: {}, seed: SEED, chapters: [chapter([list])], guideDir: root, write: false }),
      /orphan.*zh-TW[\\/]01-demo\.md.*no capture/,
    )
    assert.equal(stub.requests.length, 0)
    await stub.close()
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('a capture with no marker in one language fails naming id and file', async () => {
  const root = makeGuide({ zh: BLOCK('demo-list'), en: '# nothing\n' })
  try {
    await assert.rejects(run(root, [list]), /demo-list.*no marker.*en[\\/]01-demo\.md/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('blocks that would differ between the languages fail and nothing is written', async () => {
  const zh = BLOCK('demo-list', 'old', '```text')
  const en = BLOCK('demo-list', 'old', '```json')
  const root = makeGuide({ zh, en })
  try {
    await assert.rejects(run(root, [list], { write: true }), /demo-list.*differ/)
    assert.equal(readFileSync(join(root, 'zh-TW', '01-demo.md'), 'utf8'), zh)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('an unreachable host fails with a message naming the base URL', async () => {
  const text = BLOCK('demo-list')
  const root = makeGuide({ zh: text, en: text })
  try {
    const stub = await startStub(api)
    const baseUrl = stub.baseUrl
    await stub.close()
    await assert.rejects(
      runCapture({ baseUrl, vars: {}, seed: SEED, chapters: [chapter([list])], guideDir: root, write: false }),
      new RegExp(`unreachable.*${baseUrl.replace(/[.]/g, '\\.')}`),
    )
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('a verify hook that throws stops the run with its message', async () => {
  const text = BLOCK('demo-list')
  const root = makeGuide({ zh: text, en: text })
  try {
    const seed = [
      {
        method: 'GET',
        path: '/list',
        expect: 200,
        verify: (json) => {
          if (json.meta.total > 0) throw new CaptureError('database is not fresh')
        },
      },
    ]
    await assert.rejects(run(root, [list], { seed }), /database is not fresh/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})
