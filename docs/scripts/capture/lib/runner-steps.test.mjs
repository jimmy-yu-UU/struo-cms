import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { test } from 'node:test'
import { FIXTURE_PASSWORD, FIXTURE_TOKEN } from '../test-support/fixtures.mjs'
import { startStub } from '../test-support/stub-server.mjs'
import { CaptureError, runCapture } from './runner.mjs'

const DEMO = '01-demo.md'
const OTHER = '02-other.md'
const BLOCK = (id) => `<!-- capture: ${id} -->\n\`\`\`text\nold\n\`\`\`\n`
const noGuard = async () => {}
const OK = () => ({ status: 200, body: '{"ok":true}' })
const PNG_HEAD = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 64, 0, 0, 0, 48, 1, 2, 3]
const TOKEN_REPLY = JSON.stringify({ data: { token: FIXTURE_TOKEN } })
const tokenHandler = (request) => (request.path === '/token' ? { status: 200, body: TOKEN_REPLY } : { status: 200, body: '{}' })

function makeRoot(ids) {
  const root = mkdtempSync(join(tmpdir(), 'struo-steps-'))
  const text = ids.map(BLOCK).join('\n')
  for (const dir of ['zh-TW', 'en']) {
    mkdirSync(join(root, dir))
    for (const name of [DEMO, OTHER]) writeFileSync(join(root, dir, name), text)
  }
  mkdirSync(join(root, 'fixtures'))
  writeFileSync(join(root, 'fixtures', 'note.txt'), 'twelve bytes')
  return root
}

async function run(root, steps, over = {}, handler = OK) {
  const stub = await startStub(handler)
  try {
    const result = await runCapture({
      baseUrl: stub.baseUrl,
      vars: { adminPassword: FIXTURE_PASSWORD, lockEmail: 'probe@example.com' },
      groups: [{ name: 'g', file: DEMO, steps }],
      guideDir: root,
      fixturesDir: join(root, 'fixtures'),
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

async function inRoot(ids, fn) {
  const root = makeRoot(ids)
  try {
    return await fn(root)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
}

const upload = { field: 'file', fixture: 'note.txt', contentType: 'text/plain' }
const NOTE = '(multipart: file=@note.txt; type=text/plain)'

test('an upload step sends the fixture as one multipart file part with the declared part type', () =>
  inRoot(['demo-up'], async (root) => {
    const step = { capture: 'demo-up', method: 'POST', path: '/files', upload, expect: 201, print: { note: NOTE } }
    const result = await run(root, [step], {}, () => ({ status: 201, body: '{"success":true}' }))
    const request = result.stub.requests[0]
    assert.match(request.headers['content-type'], /^multipart\/form-data; boundary=/)
    const text = request.rawBody.toString('latin1')
    assert.ok(text.includes('name="file"; filename="note.txt"'))
    assert.ok(text.includes('Content-Type: text/plain\r\n\r\ntwelve bytes\r\n'))
    assert.equal(result.blocks.get('demo-up'), `$ POST /files ${NOTE}\n{"success":true}\nHTTP_STATUS:201`)
  }))

test('an upload of a missing fixture, or a fixture name with a path, fails before any request', () =>
  inRoot(['demo-up'], async (root) => {
    for (const fixture of ['absent.txt', '../note.txt', 'sub/note.txt']) {
      const step = { capture: 'demo-up', method: 'POST', path: '/files', upload: { ...upload, fixture }, expect: 201 }
      await assert.rejects(run(root, [step]), (error) => {
        assert.ok(error instanceof CaptureError)
        assert.equal(error.stub.requests.length, 0)
        return true
      })
    }
  }))

test('print.binary prints the declared summary of the raw bytes, with only the named headers', () =>
  inRoot(['demo-img'], async (root) => {
    const step = { capture: 'demo-img', method: 'GET', path: '/img', expect: 200, print: { headers: ['Content-Type'], binary: 'png' } }
    const handler = () => ({ status: 200, headers: { 'Content-Type': 'image/png', 'X-Other': 'x' }, body: Buffer.from(PNG_HEAD) })
    const result = await run(root, [step], {}, handler)
    assert.equal(
      result.blocks.get('demo-img'),
      [
        '$ GET /img',
        'HTTP/1.1 200 OK',
        'Content-Type: image/png',
        '',
        '# decoded PNG IHDR: width=64, height=48',
        '# body: 27 bytes (binary, not shown)',
        'HTTP_STATUS:200',
      ].join('\n'),
    )
  }))

test('print.binary refuses a transform on the same step and a body that is not the declared kind', () =>
  inRoot(['demo-img'], async (root) => {
    const base = { capture: 'demo-img', method: 'GET', path: '/img', expect: 200 }
    const png = () => ({ status: 200, body: Buffer.from(PNG_HEAD) })
    await assert.rejects(run(root, [{ ...base, print: { binary: 'png' }, transforms: ['stripTraceId'] }], {}, png), /binary/)
    await assert.rejects(run(root, [{ ...base, print: { binary: 'png' } }], {}, () => ({ status: 200, body: 'text' })), /PNG/)
  }))

test('print.pretty appends the indented copy; the raw line stays what the server sent', () =>
  inRoot(['demo-pretty'], async (root) => {
    const step = { capture: 'demo-pretty', method: 'GET', path: '/p', expect: 200, print: { pretty: true } }
    const result = await run(root, [step], {}, () => ({ status: 200, body: '{"a":{"b":1}}' }))
    assert.equal(
      result.blocks.get('demo-pretty'),
      '$ GET /p\n{"a":{"b":1}}\nHTTP_STATUS:200\npretty:\n{\n  "a": {\n    "b": 1\n  }\n}',
    )
  }))

test('print.pretty on a body that cannot be re-indented fails the step', () =>
  inRoot(['demo-pretty'], async (root) => {
    const step = { capture: 'demo-pretty', method: 'GET', path: '/p', expect: 200, print: { pretty: true } }
    await assert.rejects(run(root, [step], {}, () => ({ status: 200, body: 'plain text' })), /demo-pretty/)
  }))

test('the printed note takes {{variables}}, and a mask turns the secret in it into its placeholder', () =>
  inRoot(['demo-bearer'], async (root) => {
    const steps = [
      { method: 'POST', path: '/token', expect: 200, secret: { token: 'data.token' } },
      {
        capture: 'demo-bearer',
        method: 'GET',
        path: '/read',
        bearer: '{{token}}',
        expect: 200,
        print: { note: ' Authorization: Bearer {{token}}', mask: { token: '<token>' } },
      },
    ]
    const result = await run(root, steps, {}, tokenHandler)
    assert.equal(result.blocks.get('demo-bearer').split('\n')[0], '$ GET /read  Authorization: Bearer <token>')
    assert.equal(result.stub.requests[1].headers.authorization, `Bearer ${FIXTURE_TOKEN}`)
  }))

test('a printed note that holds a secret is refused unless it is masked', () =>
  inRoot(['demo-bearer'], async (root) => {
    const steps = [
      { method: 'POST', path: '/token', expect: 200, secret: { token: 'data.token' } },
      { capture: 'demo-bearer', method: 'GET', path: '/read', bearer: '{{token}}', expect: 200, print: { note: 'Bearer {{token}}' } },
    ]
    await assert.rejects(run(root, steps, {}, tokenHandler), (error) => {
      assert.match(error.message, /secret/)
      assert.ok(!error.message.includes(FIXTURE_TOKEN))
      return true
    })
  }))

test('print.requestBody may be a function of the run variables', () =>
  inRoot(['demo-body'], async (root) => {
    const step = {
      capture: 'demo-body',
      method: 'POST',
      path: '/b',
      body: (vars) => JSON.stringify({ email: vars.lockEmail }),
      expect: 200,
      print: { requestBody: () => '{"email":"$TW"}' },
    }
    const result = await run(root, [step])
    assert.equal(result.stub.requests[0].body, '{"email":"probe@example.com"}')
    assert.ok(result.blocks.get('demo-body').includes('body:\n{"email":"$TW"}\n'))
  }))

test('a run variable named in secretVars is a secret: refused in a block, and an unknown name is an error', () =>
  inRoot(['demo-leak'], async (root) => {
    const leak = { capture: 'demo-leak', method: 'GET', path: '/leak', expect: 200 }
    const echoed = 'editor-run-value-1'
    const over = { vars: { adminPassword: FIXTURE_PASSWORD, editorKey: echoed }, secretVars: ['editorKey'] }
    await assert.rejects(run(root, [leak], over, () => ({ status: 200, body: JSON.stringify({ echo: echoed }) })), /secret/)
    await assert.rejects(run(root, [leak], { ...over, secretVars: ['nothing'] }), /nothing/)
  }))

const cliStep = (over = {}) => ({
  capture: 'demo-cli',
  cli: { program: 'dotnet Host.dll', args: ['cmd', '--x', 'y'], env: {}, exit: 0, ...over },
})

async function withResults(files, fn) {
  const dir = mkdtempSync(join(tmpdir(), 'struo-results-'))
  try {
    for (const [name, content] of Object.entries(files)) writeFileSync(join(dir, name), content)
    return await fn(dir)
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

test('a command step writes the command line and the saved output into both languages, sending nothing', () =>
  inRoot(['demo-cli'], (root) =>
    withResults({ 'demo-cli.out': 'it worked\n', 'demo-cli.exit': '0\n' }, async (dir) => {
      const result = await run(root, [cliStep()], { cliResults: dir, write: true })
      assert.equal(result.stub.requests.length, 0)
      for (const lang of ['zh-TW', 'en']) {
        const text = readFileSync(join(root, lang, DEMO), 'utf8')
        assert.ok(text.includes('```text\n$ dotnet Host.dll cmd --x y\nit worked\n```'))
      }
    }),
  ))

test('a command step without results, or with another exit code, refuses the run before any request', () =>
  inRoot(['demo-cli'], async (root) => {
    const first = { method: 'GET', path: '/first', expect: 200 }
    await assert.rejects(run(root, [first, cliStep()]), (error) => {
      assert.match(error.message, /--cli-results/)
      assert.equal(error.stub.requests.length, 0)
      return true
    })
    await withResults({ 'demo-cli.out': 'x\n', 'demo-cli.exit': '3\n' }, async (dir) => {
      await assert.rejects(run(root, [first, cliStep()], { cliResults: dir }), (error) => {
        assert.match(error.message, /exit code 3/)
        assert.equal(error.stub.requests.length, 0)
        return true
      })
    })
  }))

test('a command step in a file that is not selected needs no results', () =>
  inRoot(['demo-up'], async (root) => {
    const groups = [
      { name: 'a', file: DEMO, steps: [{ capture: 'demo-up', method: 'GET', path: '/x', expect: 200 }] },
      { name: 'b', file: OTHER, steps: [{ ...cliStep(), capture: 'other-cli' }] },
    ]
    const result = await run(root, [], { groups, only: [DEMO] })
    assert.ok(result.blocks.has('demo-up'))
    assert.ok(!result.blocks.has('other-cli'))
  }))
