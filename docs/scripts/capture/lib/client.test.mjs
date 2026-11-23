import assert from 'node:assert/strict'
import { test } from 'node:test'
import { ConnectionError, HostUnreachableError, RequestTimeoutError, createClient } from './client.mjs'
import { startStub } from '../test-support/stub-server.mjs'

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

const cookies = (values) => (request) =>
  request.path === '/set'
    ? { status: 200, headers: { 'Set-Cookie': values } }
    : { status: 200, body: request.headers.cookie ?? '' }

test('several Set-Cookie headers in one response all enter the jar', async () => {
  await withStub(cookies(['a=1; path=/', 'b=2; path=/; httponly']), async (_stub, client) => {
    await client.request({ identity: 'u', method: 'GET', path: '/set' })
    const res = await client.request({ identity: 'u', method: 'GET', path: '/echo' })
    assert.equal(res.body, 'a=1; b=2')
  })
})

test('Max-Age=0 and an expiry in the past delete a cookie; a later value replaces it', async () => {
  let phase = 0
  const handler = (request) => {
    if (request.path === '/echo') return { status: 200, body: request.headers.cookie ?? '' }
    const sets = [
      ['a=1; path=/', 'b=2; path=/', 'c=3; path=/'],
      ['a=; path=/; Max-Age=0', 'b=gone; path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT', 'c=4; path=/'],
    ]
    return { status: 200, headers: { 'Set-Cookie': sets[phase++] } }
  }
  await withStub(handler, async (_stub, client) => {
    await client.request({ identity: 'u', method: 'GET', path: '/set' })
    await client.request({ identity: 'u', method: 'GET', path: '/set' })
    assert.equal((await client.request({ identity: 'u', method: 'GET', path: '/echo' })).body, 'c=4')
  })
})

test('a request that gets no answer fails with a timeout error after the step\'s own limit', async () => {
  await withStub(() => ({ hang: true }), async (_stub, client) => {
    const started = Date.now()
    await assert.rejects(client.request({ method: 'GET', path: '/x', timeoutMs: 150 }), (error) => {
      assert.ok(error instanceof RequestTimeoutError)
      assert.match(error.message, /no answer within 150 ms/)
      return true
    })
    assert.ok(Date.now() - started < 3000)
  })
})

test('the client default limit applies when a step names none', async () => {
  const stub = await startStub(() => ({ hang: true }))
  const client = createClient({ baseUrl: stub.baseUrl, timeoutMs: 120 })
  try {
    await assert.rejects(client.request({ method: 'GET', path: '/x' }), /no answer within 120 ms/)
  } finally {
    client.close()
    await stub.close()
  }
})

test('an ECONNRESET on a reused socket is retried once; on a new socket it is not', async () => {
  let flaky = 0
  const handler = (request) => {
    if (request.path === '/flaky') {
      flaky += 1
      return flaky === 1 ? { destroy: true } : { status: 200, body: 'second try' }
    }
    return { status: 200, body: 'ok' }
  }
  await withStub(handler, async (stub, client) => {
    await client.request({ method: 'GET', path: '/warm' })
    const res = await client.request({ method: 'GET', path: '/flaky' })
    assert.equal(res.body, 'second try')
    assert.equal(stub.requests.filter((r) => r.path === '/flaky').length, 2)
  })
  await withStub(() => ({ destroy: true }), async (stub, client) => {
    await assert.rejects(client.request({ method: 'GET', path: '/first' }), ConnectionError)
    assert.equal(stub.requests.length, 1)
  })
})

test('a refused connection reads as an unreachable host and any other socket error as a failed connection', async () => {
  const stub = await startStub(() => ({ destroy: true }))
  const client = createClient({ baseUrl: stub.baseUrl })
  try {
    await assert.rejects(client.request({ method: 'GET', path: '/' }), (error) => {
      assert.ok(error instanceof ConnectionError)
      assert.ok(!(error instanceof HostUnreachableError))
      assert.match(error.message, /connection to http:\/\/127\.0\.0\.1:\d+ failed \(ECONNRESET\)/)
      return true
    })
  } finally {
    client.close()
    await stub.close()
  }
})
