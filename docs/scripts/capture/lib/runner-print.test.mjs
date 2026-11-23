import assert from 'node:assert/strict'
import { test } from 'node:test'
import { FIXTURE_COOKIE, FIXTURE_TOKEN } from '../test-support/fixtures.mjs'
import { inRoot, run } from '../test-support/run-helpers.mjs'

const LENGTH = 'Content-Length'
const TYPE = 'Content-Type'
const UP_ID = 'demo-up'
const HEADERS_ID = 'demo-headers'
const TRACE = 'a1b2c3d4e5f60718293a4b5c6d7e8f90'
const ERROR_BODY = `{"success":false,"error":{"code":"X","message":"m","traceId":"${TRACE}"}}`
const SET_COOKIE = 'Set-Cookie'
const sessionHeader = `sess=${FIXTURE_COOKIE}; path=/; httponly`

test('an upload prints a note made from its declaration, then any note the step adds', () =>
  inRoot([UP_ID], async (root) => {
    const upload = { field: 'file', fixture: 'note.txt', contentType: 'text/plain' }
    const base = { capture: UP_ID, method: 'POST', path: '/files', upload, expect: 201 }
    const created = () => ({ status: 201, body: '{}' })
    const plain = await run(root, [base], {}, created)
    assert.equal(plain.blocks.get(UP_ID).split('\n')[0], '$ POST /files (multipart: file=@note.txt; type=text/plain)')
    const extra = await run(root, [{ ...base, print: { note: '(second note)' } }], {}, created)
    assert.equal(
      extra.blocks.get(UP_ID).split('\n')[0],
      '$ POST /files (multipart: file=@note.txt; type=text/plain) (second note)',
    )
  }))

test('a step that strips the body and prints Content-Length is refused, naming the capture', () =>
  inRoot([HEADERS_ID], async (root) => {
    const base = { capture: HEADERS_ID, method: 'GET', path: '/e', expect: 400, transforms: ['stripTraceId'] }
    const lengthHeaders = { [LENGTH]: String(ERROR_BODY.length), [TYPE]: 'application/json' }
    const answer = () => ({ status: 400, headers: lengthHeaders, body: ERROR_BODY })
    for (const headers of [true, [LENGTH, TYPE]]) {
      await assert.rejects(run(root, [{ ...base, print: { headers } }], {}, answer), (error) => {
        assert.match(error.message, new RegExp(`${HEADERS_ID}.*Content-Length`))
        return true
      })
    }
    const result = await run(root, [{ ...base, print: { headers: [TYPE] } }], {}, () => ({
      status: 400,
      headers: { [TYPE]: 'application/json' },
      body: ERROR_BODY,
    }))
    assert.ok(result.blocks.get(HEADERS_ID).includes('Content-Type: application/json'))
    assert.ok(!result.blocks.get(HEADERS_ID).includes(TRACE))
  }))

test('a step that prints Content-Length without changing the body is not refused', () =>
  inRoot([HEADERS_ID], async (root) => {
    const step = { capture: HEADERS_ID, method: 'GET', path: '/e', expect: 200, print: { headers: true } }
    const body = '{"ok":true}'
    const result = await run(root, [step], {}, () => ({ status: 200, headers: { [LENGTH]: String(body.length) }, body }))
    assert.ok(result.blocks.get(HEADERS_ID).includes('Content-Length: 11'))
  }))

test('a header list that names a header the response lacks fails, naming the capture and the header', () =>
  inRoot([HEADERS_ID], async (root) => {
    const step = { capture: HEADERS_ID, method: 'GET', path: '/e', expect: 200, print: { headers: ['X-Missing'] } }
    await assert.rejects(run(root, [step]), (error) => {
      assert.match(error.message, new RegExp(`${HEADERS_ID}.*X-Missing`))
      return true
    })
  }))

test('a cookie an identity-less step receives is a secret: refused unprinted-masked, written when masked', () =>
  inRoot([HEADERS_ID], async (root) => {
    const handler = () => ({ status: 200, headers: { [SET_COOKIE]: sessionHeader }, body: '{}' })
    const step = { capture: HEADERS_ID, method: 'GET', path: '/c', expect: 200, print: { headers: true } }
    await assert.rejects(run(root, [step], {}, handler), (error) => {
      assert.match(error.message, /secret/)
      assert.ok(!error.message.includes(FIXTURE_COOKIE))
      return true
    })
    const masked = { ...step, print: { headers: true, mask: { session_anonymous: '…' } } }
    const result = await run(root, [masked], {}, handler)
    assert.ok(result.blocks.get(HEADERS_ID).includes('Set-Cookie: sess=…; path=/; httponly'))
    assert.ok(!result.blocks.get(HEADERS_ID).includes(FIXTURE_COOKIE))
  }))

test('a verifyResponse failure that quotes the response never prints the token the step registers', () =>
  inRoot([HEADERS_ID], async (root) => {
    const step = {
      name: 'issue',
      method: 'POST',
      path: '/token',
      expect: 200,
      secret: { token: 'data.token' },
      verifyResponse: (response) => {
        throw new Error(`unexpected ${response.body}`)
      },
    }
    const handler = () => ({ status: 200, body: JSON.stringify({ data: { token: FIXTURE_TOKEN } }) })
    await assert.rejects(run(root, [step], {}, handler), (error) => {
      assert.match(error.message, /unexpected/)
      assert.ok(!error.message.includes(FIXTURE_TOKEN))
      return true
    })
  }))
