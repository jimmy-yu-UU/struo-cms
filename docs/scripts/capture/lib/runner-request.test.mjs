import assert from 'node:assert/strict'
import { test } from 'node:test'
import { inRoot, run } from '../test-support/run-helpers.mjs'
import { inOrder } from './sequence.mjs'

const ID = 'demo-headers-req'
const ACCEPT = 'Accept'
const NOTE = '(Accept: application/json)'
const send = (extra) => ({ capture: ID, method: 'GET', path: '/g', expect: 200, ...extra })

test('a step sends the extra request headers it declares', () =>
  inRoot([ID], async (root) => {
    const step = send({ headers: { [ACCEPT]: 'application/json' }, print: { note: NOTE } })
    const result = await run(root, [step])
    assert.equal(result.stub.requests[0].headers.accept, 'application/json')
    assert.equal(result.blocks.get(ID).split('\n')[0], `$ GET /g ${NOTE}`)
  }))

test('a capture step that declares headers must declare a note, naming the capture before any request', () =>
  inRoot([ID], async (root) => {
    await assert.rejects(run(root, [send({ headers: { [ACCEPT]: '*/*' } })]), (error) => {
      assert.match(error.message, new RegExp(`${ID}.*note`))
      assert.equal(error.stub.requests.length, 0)
      return true
    })
  }))

test('a setup step with headers needs no note', () =>
  inRoot([ID], async (root) => {
    const setup = { name: 'probe', method: 'GET', path: '/g', expect: 200, headers: { [ACCEPT]: '*/*' } }
    const result = await run(root, [setup, send({})])
    assert.equal(result.stub.requests[0].headers.accept, '*/*')
  }))

test('headers the client controls cannot be overridden, whatever the case, and nothing is sent', () =>
  inRoot([ID], async (root) => {
    const owned = ['Content-Type', 'cookie', 'AUTHORIZATION', 'x-struo-csrf', 'content-length', 'Transfer-Encoding', 'host']
    await inOrder(owned, (name) => {
      const step = { name: 'probe', method: 'GET', path: '/g', expect: 200, headers: { [name]: 'x' } }
      return assert.rejects(run(root, [step]), (error) => {
        assert.match(error.message, new RegExp(name, 'i'))
        assert.equal(error.stub.requests.length, 0)
        return true
      })
    })
  }))
