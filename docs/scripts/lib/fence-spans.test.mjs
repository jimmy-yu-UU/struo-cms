import assert from 'node:assert/strict'
import { test } from 'node:test'
import { closesFence, fenceOpener, fencedLines } from './fences.mjs'

test('fenceOpener returns the fence run of an opener and null for any other line', () => {
  assert.equal(fenceOpener('```text'), '```')
  assert.equal(fenceOpener('   ~~~~'), '~~~~')
  assert.equal(fenceOpener('    ```'), null)
  assert.equal(fenceOpener('plain'), null)
})

test('closesFence needs the same character and at least the opener length', () => {
  assert.equal(closesFence('```', '```'), true)
  assert.equal(closesFence('```', '`````'), true)
  assert.equal(closesFence('````', '```'), false)
  assert.equal(closesFence('```', '~~~'), false)
  assert.equal(closesFence('```', 'text'), false)
})

test('fencedLines agrees with fenceOpener and closesFence on every line of a sample', () => {
  const lines = ['a', '```x', '~~~', '```', 'b', '~~~~', '```', '~~~~', 'c']
  assert.deepEqual(fencedLines(lines), [false, true, true, true, false, true, true, true, false])
})
