import assert from 'node:assert/strict'
import { test } from 'node:test'
import { FIXTURE_COOKIE } from '../test-support/fixtures.mjs'
import { inRoot, run } from '../test-support/run-helpers.mjs'

const LENGTH = 'Content-Length'
const ID = 'demo-length'
const SET_COOKIE = 'Set-Cookie'
const PNG_HEAD = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 64, 0, 0, 0, 48, 1, 2, 3]
const MASK = { session_anonymous: '<c>' }

const cookieBody = `{"v":"${FIXTURE_COOKIE}"}`
const withCookie = () => ({
  status: 200,
  headers: { [SET_COOKIE]: `sess=${FIXTURE_COOKIE}; path=/`, [LENGTH]: String(cookieBody.length) },
  body: cookieBody,
})
const step = (print, extra = {}) => ({ capture: ID, method: 'GET', path: '/c', expect: 200, print, ...extra })

test('a mask that shortens the body makes a shown Content-Length false, so the step is refused', () =>
  inRoot([ID], async (root) => {
    await assert.rejects(run(root, [step({ headers: true, mask: MASK })], {}, withCookie), (error) => {
      assert.match(error.message, new RegExp(`${ID}.*Content-Length`))
      assert.ok(!error.message.includes(FIXTURE_COOKIE))
      return true
    })
    await assert.rejects(run(root, [step({ headers: [LENGTH], mask: MASK })], {}, withCookie), /Content-Length/)
  }))

test('a mask that finds nothing in the body leaves Content-Length true, so the step is accepted', () =>
  inRoot([ID], async (root) => {
    const plain = '{"v":1}'
    const handler = () => ({ status: 200, headers: { [LENGTH]: String(plain.length), [SET_COOKIE]: `sess=${FIXTURE_COOKIE}` }, body: plain })
    const result = await run(root, [step({ headers: [LENGTH, SET_COOKIE], mask: MASK })], {}, handler)
    assert.ok(result.blocks.get(ID).includes('Content-Length: 7'))
    assert.ok(result.blocks.get(ID).includes('Set-Cookie: sess=<c>'))
  }))

test('a binary summary may show Content-Length: it prints no body and states the byte count', () =>
  inRoot([ID], async (root) => {
    const bytes = Buffer.from(PNG_HEAD)
    const handler = () => ({ status: 200, headers: { [LENGTH]: String(bytes.length), 'Content-Type': 'image/png' }, body: bytes })
    const result = await run(root, [step({ headers: [LENGTH], binary: 'png' })], {}, handler)
    assert.ok(result.blocks.get(ID).includes('Content-Length: 27'))
    assert.ok(result.blocks.get(ID).includes('# body: 27 bytes (binary, not shown)'))
  }))

test('two cookies one identity holds are both covered by one mask name', () =>
  inRoot([ID], async (root) => {
    const second = `${FIXTURE_COOKIE}-two`
    const handler = () => ({
      status: 200,
      headers: { [SET_COOKIE]: [`a=${FIXTURE_COOKIE}; path=/`, `b=${second}; path=/`] },
      body: '{}',
    })
    await assert.rejects(run(root, [step({ headers: true })], {}, handler), /secret/)
    const result = await run(root, [step({ headers: true, mask: MASK })], {}, handler)
    const text = result.blocks.get(ID)
    assert.ok(text.includes('Set-Cookie: a=<c>; path=/') && text.includes('Set-Cookie: b=<c>; path=/'))
    assert.ok(!text.includes(FIXTURE_COOKIE))
  }))
