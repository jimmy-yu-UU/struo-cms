import assert from 'node:assert/strict'
import { test } from 'node:test'
import { CaptureError, runCapture } from '../lib/runner.mjs'
import { startStub } from '../lib/stub-server.mjs'
import { TRANSFORMS } from '../lib/transforms.mjs'
import { CHAPTERS, SEED } from './index.mjs'

const allSteps = () => [...SEED, ...CHAPTERS.flatMap((chapter) => chapter.steps)]

test('every step declares a method, a path and an expected status', () => {
  for (const step of allSteps()) {
    assert.ok(step.method && step.path, `${step.capture ?? step.name}: method and path`)
    assert.ok(Number.isInteger(step.expect), `${step.capture ?? step.name}: expect`)
  }
})

test('the tool never sends DELETE: cleanup is the operator\'s job', () => {
  assert.deepEqual(allSteps().filter((step) => step.method === 'DELETE'), [])
  assert.deepEqual(allSteps().filter((step) => /purge/i.test(String(step.path))), [])
})

test('capture ids are unique across chapters, except for the steps of one block', () => {
  const owner = new Map()
  for (const chapter of CHAPTERS) {
    for (const step of chapter.steps.filter((s) => s.capture)) {
      assert.ok(!owner.has(step.capture) || owner.get(step.capture) === chapter.number, step.capture)
      owner.set(step.capture, chapter.number)
    }
  }
})

test('every declared transform exists', () => {
  for (const step of allSteps()) {
    for (const name of step.transforms ?? []) assert.ok(name in TRANSFORMS, `${step.capture}: ${name}`)
  }
})

test('chapter files are numbered and distinct', () => {
  const files = CHAPTERS.map((chapter) => chapter.file)
  assert.equal(new Set(files).size, files.length)
  for (const chapter of CHAPTERS) assert.ok(chapter.file.startsWith(`${chapter.number}-`))
})

test('the seed refuses a database that already holds articles, categories or tags', async () => {
  for (const collection of ['article', 'category', 'tag']) {
    const stub = await startStub((request) => {
      if (request.path === '/api/auth/login') return { status: 200, headers: { 'Set-Cookie': 's=1; path=/' }, body: '{"success":true}' }
      const total = request.path.startsWith(`/api/items/${collection}?`) ? 1 : 0
      return { status: 200, body: `{"success":true,"data":[],"meta":{"total":${total},"limit":1,"offset":0}}` }
    })
    try {
      await assert.rejects(
        runCapture({
          baseUrl: stub.baseUrl,
          vars: { adminEmail: 'a@b.c', adminPassword: 'pw' },
          seed: SEED,
          chapters: [],
          guideDir: '.',
          write: false,
        }),
        (error) => error instanceof CaptureError && new RegExp(`already holds.*${collection}`).test(error.message),
      )
      assert.equal(stub.requests.some((r) => r.method === 'POST' && r.path !== '/api/auth/login'), false)
    } finally {
      await stub.close()
    }
  }
})

test('the seed creates the category, two tags and the three articles in order A, B, C', async () => {
  let counter = 0
  const stub = await startStub((request) => {
    if (request.path === '/api/auth/login') return { status: 200, headers: { 'Set-Cookie': 's=1; path=/' }, body: '{"success":true}' }
    if (request.method === 'GET') return { status: 200, body: '{"success":true,"data":[],"meta":{"total":0}}' }
    counter += 1
    return { status: 201, body: `{"success":true,"data":{"id":"id-${counter}"}}` }
  })
  try {
    await runCapture({
      baseUrl: stub.baseUrl,
      vars: { adminEmail: 'a@b.c', adminPassword: 'pw' },
      seed: SEED,
      chapters: [],
      guideDir: '.',
      write: false,
    })
    const posts = stub.requests.filter((r) => r.method === 'POST' && r.path.startsWith('/api/items/'))
    assert.deepEqual(
      posts.map((r) => r.path),
      ['/api/items/category', '/api/items/tag', '/api/items/tag', '/api/items/article', '/api/items/article', '/api/items/article'],
    )
    const titles = posts.slice(3).map((r) => Object.values(JSON.parse(r.body).translations)[0].title)
    assert.deepEqual(titles, ['Getting started', 'Release notes', 'Draft piece'])
    assert.equal(JSON.parse(posts[3].body).categoryId, 'id-1')
    assert.deepEqual(JSON.parse(posts[4].body).tags, ['id-2', { id: 'id-3', note: 'hero' }])
  } finally {
    await stub.close()
  }
})
