import assert from 'node:assert/strict'
import { test } from 'node:test'
import { FIXTURE_EDITOR, FIXTURE_PASSWORD } from '../test-support/fixtures.mjs'
import { startStub } from '../test-support/stub-server.mjs'
import { runCapture } from '../lib/runner.mjs'
import { TRANSFORMS } from '../lib/transforms.mjs'
import { readFileSync, readdirSync } from 'node:fs'
import { checkGuide } from '../lib/check.mjs'
import { unmarkedTranscriptBlocks } from '../test-support/transcripts.mjs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { ch14First, ch14Rest } from './ch14.mjs'
import { GROUPS } from './index.mjs'
const GRAPHQL_FILE = '14-graphql.md'

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
const LANGUAGES = ['zh-TW', 'en']

test('the transcript scan sees indented and tilde fences and a marker only directly above', () => {
  const text = [
    '<!-- capture: ok -->',
    '```text',
    '$ GET /a',
    '```',
    '',
    '   ~~~text',
    '   $ GET /b',
    '   ~~~',
    '',
    '```text',
    '  HTTP_STATUS:200',
    '```',
    '',
    '```text',
    'plain text',
    '```',
  ].join('\n')
  assert.deepEqual(unmarkedTranscriptBlocks(text), [6, 10])
})

// Every fenced block of chapters 7 to 17 that looks like a transcript sits directly under a marker.
test('no hand-written transcript block is left in chapters 7 to 17', () => {
  const unmarked = LANGUAGES.flatMap((locale) =>
    readdirSync(join(GUIDE, locale))
      .filter((name) => CAPTURED_CHAPTERS.test(name))
      .flatMap((name) =>
        unmarkedTranscriptBlocks(readFileSync(join(GUIDE, locale, name), 'utf8')).map((line) => `${locale}/${name}:${line}`),
      ),
  )
  assert.deepEqual(unmarked, [])
})

const fileOrder = () => GROUPS.map((group) => group.file ?? group.name)
const firstAt = (names, name) => names.indexOf(name)
const lastAt = (names, name) => names.lastIndexOf(name)

test('the run starts with the seed and keeps the order the shared data needs', () => {
  const names = fileOrder()
  assert.ok(names[0].startsWith('seed: the Guides'))
  const chapters = ['10-query-basics.md', '11-query-advanced.md', GRAPHQL_FILE, '12-rest-conventions.md', '13-rest-endpoints.md']
  const order = chapters.map((file) => firstAt(names, file))
  assert.deepEqual(order, [...order].sort((a, b) => a - b))
  assert.ok(lastAt(names, GRAPHQL_FILE) > firstAt(names, '13-rest-endpoints.md'))
})

test('chapters 15 to 17 come before the Tutorials data, which comes before chapters 7, 8 and 9', () => {
  const names = fileOrder()
  const tutorials = names.findIndex((name) => name.startsWith('seed: the Tutorials'))
  const before = ['15-files-and-media.md', '16-authentication.md', '17-roles-and-permissions.md'].map((file) => firstAt(names, file))
  const after = ['07-i18n.md', '08-relations.md', '09-revisions-and-trash.md'].map((file) => firstAt(names, file))
  assert.deepEqual(before, [...before].sort((a, b) => a - b))
  assert.ok(before[0] > lastAt(names, GRAPHQL_FILE))
  assert.ok(tutorials > before[2])
  assert.ok(after.every((position) => position > tutorials))
  assert.deepEqual(after, [...after].sort((a, b) => a - b))
})

const ch14Step = (capture) => [ch14First, ch14Rest].flatMap((group) => group.steps).find((step) => step.capture === capture)
const bodyOf = (step, vars) => (typeof step.body === 'function' ? step.body(vars) : step.body)

// The request text the manual's chapter 14 blocks print, byte for byte.
test('the chapter 14 request bodies are exactly the text the manual prints', () => {
  const vars = { articleAId: 'aaaa', releaseId: 'rrrr' }
  assert.equal(
    bodyOf(ch14Step('ch14-some-junction'), vars),
    String.raw`{"query": "query { articles(filter: { tags: { some: { name: { eq: \"howto\" }, junction: { note: { eq: \"hero\" } } } } }) { total items { id status tagsLinks { node { name } junction { note } } } } }"}`,
  )
  assert.equal(
    bodyOf(ch14Step('ch14-update-tagslinks'), vars),
    String.raw`{"query": "mutation { updateArticle(id: \"aaaa\", input: { tagsLinks: [{ id: \"rrrr\", note: \"editor pick\" }] }) { id tagsLinks { node { name } junction { note } } } }"}`,
  )
  assert.equal(bodyOf(ch14Step('ch14-anon-users'), vars), '{"query":"{ users { total } }"}')
  assert.equal(bodyOf(ch14Step('ch14-bogus-field-400'), vars), '{"query": "query { bogusThing { id } }"}')
})
