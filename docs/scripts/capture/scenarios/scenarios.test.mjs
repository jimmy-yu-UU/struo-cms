import assert from 'node:assert/strict'
import { test } from 'node:test'
import { FIXTURE_PASSWORD } from '../test-support/fixtures.mjs'
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
    assert.ok(Number.isInteger(step.expect), `${step.capture ?? step.name}: expect`)
    for (const name of step.transforms ?? []) assert.ok(name in TRANSFORMS, `${step.capture}: ${name}`)
  }
})

test('the registry matches the markers in the manual in both languages', () => {
  const { problems } = checkGuide({ guideDir: GUIDE, groups: GROUPS })
  assert.deepEqual(problems, [])
})

test('the seed creates the category, two tags and the three articles in order A, B, C', async () => {
  let counter = 0
  const stub = await startStub((request) => {
    if (request.method === 'GET') return { status: 200, body: '{"success":true,"data":[],"meta":{"total":0}}' }
    counter += 1
    return { status: 201, body: `{"success":true,"data":{"id":"id-${counter}"}}` }
  })
  try {
    const seed = GROUPS.filter((group) => !group.steps.some((step) => step.capture))
    await runCapture({
      baseUrl: stub.baseUrl,
      vars: { adminEmail: 'a@b.c', adminPassword: FIXTURE_PASSWORD },
      groups: seed,
      guideDir: '.',
      write: false,
      guard: async () => {},
    })
    const posts = stub.requests.filter((r) => r.method === 'POST')
    assert.deepEqual(
      posts.map((r) => r.path),
      ['/api/items/category', '/api/items/tag', '/api/items/tag', ARTICLES, ARTICLES, ARTICLES],
    )
    const titles = posts.slice(3).map((r) => Object.values(JSON.parse(r.body).translations)[0].title)
    assert.deepEqual(titles, ['Getting started', 'Release notes', 'Draft piece'])
    assert.equal(JSON.parse(posts[3].body).categoryId, 'id-1')
    assert.deepEqual(JSON.parse(posts[3].body).tags, [{ id: 'id-2', note: 'hero' }])
    assert.deepEqual(JSON.parse(posts[4].body).tags, ['id-2', { id: 'id-3', note: 'hero' }])
    assert.equal(JSON.parse(posts[5].body).status, 'draft')
  } finally {
    await stub.close()
  }
})
