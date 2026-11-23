import assert from 'node:assert/strict'
import { test } from 'node:test'
import { startStub } from '../test-support/stub-server.mjs'
import { FIXTURE_PASSWORD } from '../test-support/fixtures.mjs'
import { createClient } from './client.mjs'
import { checkFresh } from './fresh.mjs'

const vars = { adminEmail: 'a@b.c', adminPassword: FIXTURE_PASSWORD }

// totals: { user, file, article, ... }; a total is reported only when the list asks for trashed rows too.
function host(totals, { login = 200 } = {}) {
  return (request) => {
    if (request.path === '/api/auth/login') {
      return login === 200
        ? { status: 200, headers: { 'Set-Cookie': 's=1; path=/' }, body: '{"success":true}' }
        : { status: login, body: '{"success":false}' }
    }
    const collection = /^\/api\/items\/([A-Za-z]+)\?/.exec(request.path)?.[1]
    const total = request.path.includes('deleted=with') ? (totals[collection] ?? 0) : 0
    return { status: 200, body: `{"success":true,"data":[],"meta":{"total":${total},"limit":1,"offset":0}}` }
  }
}

async function check(totals, options) {
  const stub = await startStub(host(totals, options))
  const client = createClient({ baseUrl: stub.baseUrl })
  try {
    await checkFresh({ client, vars, collections: ['article', 'category', 'tag'] })
    return stub
  } catch (error) {
    error.stub = stub
    throw error
  } finally {
    client.close()
    await stub.close()
  }
}

test('a database with the one bootstrap user and no files or content is fresh', async () => {
  const stub = await check({ user: 1 })
  const writes = stub.requests.filter((r) => r.method !== 'GET')
  assert.deepEqual(writes.map((r) => r.path), ['/api/auth/login'])
})

test('every list asks for trashed rows as well', async () => {
  const stub = await check({ user: 1 })
  const lists = stub.requests.filter((r) => r.method === 'GET')
  assert.ok(lists.length >= 5)
  for (const list of lists) assert.match(list.path, /deleted=with/)
})

test('a trashed article is found, and the message says what was found', async () => {
  await assert.rejects(check({ user: 1, article: 1 }), (error) => {
    assert.match(error.message, /not freshly migrated/)
    assert.match(error.message, /1 article row\(s\) \(trashed rows included\)/)
    return true
  })
})

test('a second user, a file and content in every collection are all reported together', async () => {
  await assert.rejects(check({ user: 2, file: 3, article: 1, category: 4, tag: 5 }), (error) => {
    for (const part of ['2 users (expected exactly 1)', '3 file row(s)', '1 article row(s)', '4 category row(s)', '5 tag row(s)']) {
      assert.ok(error.message.includes(part), part)
    }
    return true
  })
})

test('no user at all is not fresh either', async () => {
  await assert.rejects(check({ user: 0 }), /0 users \(expected exactly 1\)/)
})

test('a refused login stops the guard with the status', async () => {
  await assert.rejects(check({ user: 1 }, { login: 401 }), /login as the bootstrap admin.*HTTP 401/)
})
