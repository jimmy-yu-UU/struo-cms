import assert from 'node:assert/strict'
import { test } from 'node:test'
import { fencedLines } from './fences.mjs'

test('a backtick fence marks its opener, body and closer; prose around it is not fenced', () => {
  const lines = ['prose', '```js', 'const a = {{ x }}', '```', 'more prose']
  assert.deepEqual(fencedLines(lines), [false, true, true, true, false])
})

test('a tilde fence works the same way', () => {
  const lines = ['~~~', '| a | b |', '~~~', 'after']
  assert.deepEqual(fencedLines(lines), [true, true, true, false])
})

test('a closer must reuse the opener character: ~~~ does not close a ``` fence', () => {
  const lines = ['```', '~~~', 'still inside', '```', 'out']
  assert.deepEqual(fencedLines(lines), [true, true, true, true, false])
})

test('a closer must be at least as long as its opener', () => {
  const lines = ['````', '```', 'still inside', '`````', 'out']
  assert.deepEqual(fencedLines(lines), [true, true, true, true, false])
})

test('an opener indented up to three spaces counts; four spaces is indented code, not a fence', () => {
  assert.deepEqual(fencedLines(['   ```', 'x', '   ```', 'y']), [true, true, true, false])
  assert.deepEqual(fencedLines(['    ```', 'x']), [false, false])
})

test('an unclosed fence runs to the end of the input', () => {
  assert.deepEqual(fencedLines(['```', 'a', 'b']), [true, true, true])
})

test('empty input yields an empty array', () => {
  assert.deepEqual(fencedLines([]), [])
})
