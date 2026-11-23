import assert from 'node:assert/strict'
import { test } from 'node:test'
import { selectFiles, summarize } from './select.mjs'

const groups = [
  { name: 'a', file: '10-x.md', steps: [{ capture: 'p', method: 'GET', path: '/', expect: 200 }] },
  { name: 'b', steps: [{ capture: 'q', file: '11-y.md', method: 'GET', path: '/', expect: 200 }] },
  { name: 'c', file: '10-x.md', steps: [{ capture: 'r', method: 'GET', path: '/', expect: 200 }] },
]

test('no option selects every file with a capture, in first-use order', () => {
  assert.deepEqual(selectFiles(undefined, groups), ['10-x.md', '11-y.md'])
})

test('chapter numbers are de-duplicated and keep the order of the files, not of the option', () => {
  assert.deepEqual(selectFiles('11,10,10', groups), ['10-x.md', '11-y.md'])
  assert.deepEqual(selectFiles(' 10 , 10 ', groups), ['10-x.md'])
})

test('a number with no capture is an error', () => {
  assert.throws(() => selectFiles('99', groups), /no scenario for chapter 99/)
})

test('the summary counts the files written, not the blocks produced', () => {
  const files = [{ changed: true }, { changed: true }, { changed: false }]
  assert.equal(summarize({ write: true, files, only: ['10-x.md'] }), 'wrote 2 files (10-x.md)')
  assert.equal(summarize({ write: false, files, only: ['10-x.md', '11-y.md'] }), 'would write 2 files (10-x.md, 11-y.md)')
})
