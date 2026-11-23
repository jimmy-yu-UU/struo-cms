import assert from 'node:assert/strict'
import { test } from 'node:test'
import { FIXTURE_EDITOR, FIXTURE_PASSWORD } from '../test-support/fixtures.mjs'
import { startStub } from '../test-support/stub-server.mjs'
import { runCapture } from '../lib/runner.mjs'
import { TRANSFORMS } from '../lib/transforms.mjs'
import { checkGuide } from '../lib/check.mjs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { GROUPS } from './index.mjs'

const GUIDE = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'guide')
const ARTICLES = '/api/items/article'
const allSteps = () => GROUPS.flatMap((group) => group.steps)

test('every declared transform exists and every step declares its status', () => {
  for (const step of allSteps()) {
    if (!step.cli) assert.ok(Number.isInteger(step.expect), `${step.capture ?? step.name}: expect`)
    for (const name of step.transforms ?? []) assert.ok(name in TRANSFORMS, `${step.capture}: ${name}`)
  }
})

test('the registry matches the markers in the manual in both languages', () => {
  const { problems } = checkGuide({ guideDir: GUIDE, groups: GROUPS })
  assert.deepEqual(problems, [])
})

test('the seed creates the category, two tags, the three articles in order A, B, C, and the editor', async () => {
  let counter = 0
  const stub = await startStub((request) => {
    if (request.method === 'GET') return { status: 200, body: '{"success":true,"data":[],"meta":{"total":0}}' }
    if (request.method === 'PUT') return { status: 200, body: '{}' }
    counter += 1
    return { status: 201, body: `{"id":"id-${counter}","success":true,"data":{"id":"id-${counter}"}}` }
  })
  try {
    const seed = GROUPS.filter((group) => group.name.startsWith('seed: the Guides'))
    await runCapture({
      baseUrl: stub.baseUrl,
      vars: { adminEmail: 'a@b.c', adminPassword: FIXTURE_PASSWORD, editorPassword: FIXTURE_EDITOR },
      secretVars: ['editorPassword'],
      groups: seed,
      guideDir: '.',
      write: false,
      guard: async () => {},
    })
    const posts = stub.requests.filter((r) => r.method === 'POST')
    assert.deepEqual(
      posts.map((r) => r.path),
      [
        '/api/items/category',
        '/api/items/tag',
        '/api/items/tag',
        ARTICLES,
        ARTICLES,
        ARTICLES,
        '/api/items/role',
        '/api/users',
        '/api/items/userRole',
      ],
    )
    const titles = posts.slice(3, 6).map((r) => Object.values(JSON.parse(r.body).translations)[0].title)
    assert.deepEqual(titles, ['Getting started', 'Release notes', 'Draft piece'])
    assert.equal(JSON.parse(posts[3].body).categoryId, 'id-1')
    assert.deepEqual(JSON.parse(posts[3].body).tags, [{ id: 'id-2', note: 'hero' }])
    assert.deepEqual(JSON.parse(posts[4].body).tags, ['id-2', { id: 'id-3', note: 'hero' }])
    assert.equal(JSON.parse(posts[5].body).status, 'draft')
    assert.equal(JSON.parse(posts[6].body).name, 'editor')
    assert.equal(JSON.parse(posts[7].body).password, FIXTURE_EDITOR)
    assert.deepEqual(JSON.parse(posts[8].body), { userId: 'id-8', roleId: 'id-7' })
  } finally {
    await stub.close()
  }
})

const CAPTURED_CHAPTERS = /^(0[7-9]|1[0-7])-.*\.md$/
const TRANSCRIPT_LINE = /^(\$ |HTTP_STATUS:)/

// Every fenced block of chapters 7 to 17 that looks like a transcript sits directly under a marker.
test('no hand-written transcript block is left in chapters 7 to 17', async () => {
  const { readFileSync, readdirSync } = await import('node:fs')
  const unmarked = []
  for (const locale of ['zh-TW', 'en']) {
    const files = readdirSync(join(GUIDE, locale)).filter((name) => CAPTURED_CHAPTERS.test(name))
    for (const name of files) {
      const lines = readFileSync(join(GUIDE, locale, name), 'utf8').split('\n')
      let index = 0
      while (index < lines.length) {
        const fence = /^(`{3,})/.exec(lines[index])
        if (!fence) {
          index += 1
          continue
        }
        let close = index + 1
        while (close < lines.length && !lines[close].startsWith(fence[1])) close += 1
        const marked = index > 0 && /^<!-- capture: [\w.-]+ -->$/.test(lines[index - 1])
        if (!marked && lines.slice(index + 1, close).some((line) => TRANSCRIPT_LINE.test(line))) {
          unmarked.push(`${locale}/${name}:${index + 1}`)
        }
        index = close + 1
      }
    }
  }
  assert.deepEqual(unmarked, [])
})

test('the run keeps the order the shared data needs', () => {
  const names = GROUPS.map((group) => group.file ?? group.name)
  const at = (file, nth = 0) => names.map((name, index) => (name === file ? index : -1)).filter((index) => index >= 0)[nth]
  const order = [
    '10-query-basics.md',
    '11-query-advanced.md',
    '14-graphql.md',
    '12-rest-conventions.md',
    '13-rest-endpoints.md',
  ].map((file) => at(file))
  assert.deepEqual(order, [...order].sort((a, b) => a - b))
  assert.ok(at('14-graphql.md', 1) > at('13-rest-endpoints.md'))
  const later = ['15-files-and-media.md', '16-authentication.md', '17-roles-and-permissions.md', '07-i18n.md', '08-relations.md', '09-revisions-and-trash.md']
  const positions = later.map((file) => at(file))
  assert.deepEqual(positions, [...positions].sort((a, b) => a - b))
  assert.ok(positions[0] > at('14-graphql.md', 1))
})
