import assert from 'node:assert/strict'
import { test } from 'node:test'
import { HostUnreachableError, createClient } from './client.mjs'
import { startStub } from './stub-server.mjs'

const login = (request) =>
  request.path === '/login'
    ? { status: 200, headers: { 'Set-Cookie': 'sess=abc; path=/; httponly', 'Content-Type': 'application/json' }, body: '{"ok":true}' }
    : { status: 200, body: 'plain' }

async function withStub(handler, fn) {
  const stub = await startStub(handler)
  try {
    const client = createClient({ baseUrl: stub.baseUrl })
    try {
      await fn(stub, client)
    } finally {
      client.close()
    }
  } finally {
    await stub.close()
  }
}

test('a response comes back raw, with the status text and the headers in server order', async () => {
  await withStub(login, async (_stub, client) => {
    const res = await client.request({ identity: 'admin', method: 'POST', path: '/login', body: '{"a":1}' })
    assert.equal(res.status, 200)
    assert.equal(res.statusMessage, 'OK')
    assert.equal(res.httpVersion, '1.1')
    assert.equal(res.body, '{"ok":true}')
    assert.deepEqual(res.headers.slice(0, 2), [
      ['Set-Cookie', 'sess=abc; path=/; httponly'],
      ['Content-Type', 'application/json'],
    ])
  })
})

test('the cookie jar is per identity: admin keeps its cookie, another identity and anonymous send none', async () => {
  await withStub(login, async (stub, client) => {
    await client.request({ identity: 'admin', method: 'POST', path: '/login', body: '{}' })
    await client.request({ identity: 'admin', method: 'GET', path: '/a' })
    await client.request({ identity: 'editor', method: 'GET', path: '/b' })
    await client.request({ method: 'GET', path: '/c' })
    const [, adminGet, editorGet, anonGet] = stub.requests
    assert.equal(adminGet.headers.cookie, 'sess=abc')
    assert.equal(editorGet.headers.cookie, undefined)
    assert.equal(anonGet.headers.cookie, undefined)
  })
})

test('the CSRF header goes on every unsafe request that carries a cookie, and only those', async () => {
  await withStub(login, async (stub, client) => {
    await client.request({ identity: 'admin', method: 'POST', path: '/login', body: '{}' })
    await client.request({ identity: 'admin', method: 'GET', path: '/g' })
    await client.request({ identity: 'admin', method: 'POST', path: '/p', body: '{}' })
    await client.request({ identity: 'admin', method: 'PUT', path: '/u', body: '{}' })
    await client.request({ identity: 'admin', method: 'POST', path: '/n', body: '{}', csrf: false })
    await client.request({ identity: 'fresh', method: 'POST', path: '/f', body: '{}' })
    const byPath = Object.fromEntries(stub.requests.map((r) => [r.path, r.headers['x-struo-csrf']]))
    assert.equal(byPath['/login'], undefined)
    assert.equal(byPath['/g'], undefined)
    assert.ok(byPath['/p'])
    assert.ok(byPath['/u'])
    assert.equal(byPath['/n'], undefined)
    assert.equal(byPath['/f'], undefined)
  })
})

test('a bearer request sends the token, no cookie and no CSRF header', async () => {
  await withStub(login, async (stub, client) => {
    await client.request({ identity: 'admin', method: 'POST', path: '/login', body: '{}' })
    await client.request({ identity: 'admin', method: 'POST', path: '/b', body: '{}', bearer: 'tok' })
    const sent = stub.requests[1]
    assert.equal(sent.headers.authorization, 'Bearer tok')
    assert.equal(sent.headers.cookie, undefined)
    assert.equal(sent.headers['x-struo-csrf'], undefined)
  })
})

test('a JSON body is sent byte for byte with a JSON content type', async () => {
  await withStub(login, async (stub, client) => {
    await client.request({ method: 'POST', path: '/x', body: '{"a": 1 }' })
    assert.equal(stub.requests[0].body, '{"a": 1 }')
    assert.match(stub.requests[0].headers['content-type'], /application\/json/)
  })
})

test('an unreachable host raises HostUnreachableError naming the base URL', async () => {
  const stub = await startStub(() => ({ status: 200 }))
  const { baseUrl } = stub
  await stub.close()
  const client = createClient({ baseUrl })
  await assert.rejects(client.request({ method: 'GET', path: '/' }), (error) => {
    assert.ok(error instanceof HostUnreachableError)
    assert.ok(error.message.includes(baseUrl))
    return true
  })
})
