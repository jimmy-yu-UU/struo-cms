import assert from 'node:assert/strict'
import { test } from 'node:test'
import { fencedLines } from '../../lib/fences.mjs'
import { blockRegion, findBlocks, replaceBlock } from './markers.mjs'

const DOC = [
  '# Title',
  '',
  '<!-- capture: one -->',
  '```text',
  'old line',
  '```',
  '',
  'prose',
].join('\n')

test('findBlocks reports the id and the line numbers of marker, opening fence and closing fence', () => {
  assert.deepEqual(findBlocks(DOC), [{ id: 'one', markerLine: 2, openLine: 3, closeLine: 5 }])
})

test('a document without markers has no blocks', () => {
  assert.deepEqual(findBlocks('# T\n\n```text\nx\n```\n'), [])
})

test('a marker with no fence on the next line is an error naming the id and the line', () => {
  const text = '<!-- capture: lonely -->\n\nprose\n```text\nx\n```\n'
  assert.throws(() => findBlocks(text, { file: 'a.md' }), /a\.md:1: marker 'lonely' is not directly above a fence/)
})

test('a marker as the last line has no fence either', () => {
  assert.throws(() => findBlocks('text\n<!-- capture: last -->'), /marker 'last' is not directly above a fence/)
})

test('a fence that never closes is an error', () => {
  assert.throws(() => findBlocks('<!-- capture: open -->\n```text\nx\n'), /marker 'open': the fence is never closed/)
})

test('a marker-looking line inside a fence is content, not a marker', () => {
  const text = ['```md', '<!-- capture: inner -->', '```', '', '<!-- capture: real -->', '```text', 'x', '```'].join('\n')
  assert.deepEqual(
    findBlocks(text).map((block) => block.id),
    ['real'],
  )
})

test('the same id twice in one file is an error', () => {
  const text = ['<!-- capture: dup -->', '```text', 'a', '```', '<!-- capture: dup -->', '```text', 'b', '```'].join('\n')
  assert.throws(() => findBlocks(text), /marker 'dup' appears twice/)
})

test('a closing fence must be at least as long as the opening one', () => {
  const text = ['<!-- capture: long -->', '````text', '```', 'x', '````', 'after'].join('\n')
  assert.deepEqual(findBlocks(text), [{ id: 'long', markerLine: 0, openLine: 1, closeLine: 4 }])
})

test('findBlocks agrees with the shared fence rule on where a block ends', () => {
  const lines = ['a', '<!-- capture: x -->', '~~~text', '```', 'y', '~~~', 'b']
  const [block] = findBlocks(lines.join('\n'))
  const fenced = fencedLines(lines)
  assert.ok(fenced.slice(block.openLine, block.closeLine + 1).every(Boolean))
  assert.equal(fenced[block.closeLine + 1], false)
})

test('replaceBlock swaps only the lines between the fences', () => {
  const out = replaceBlock(DOC, 'one', 'new 1\nnew 2')
  assert.equal(out, DOC.replace('old line', 'new 1\nnew 2'))
})

test('replaceBlock keeps CRLF line endings', () => {
  const crlf = DOC.replaceAll('\n', '\r\n')
  const out = replaceBlock(crlf, 'one', 'a\nb')
  assert.equal(out, crlf.replace('old line', 'a\r\nb'))
  assert.ok(!/[^\r]\n/.test(out))
})

test('replaceBlock handles a block that ends the file, with and without a final newline', () => {
  const noEol = '<!-- capture: z -->\n```text\nold\n```'
  assert.equal(replaceBlock(noEol, 'z', 'new'), '<!-- capture: z -->\n```text\nnew\n```')
  assert.equal(replaceBlock(`${noEol}\n`, 'z', 'new'), '<!-- capture: z -->\n```text\nnew\n```\n')
})

test('replaceBlock can empty a block and refill it', () => {
  const empty = '<!-- capture: e -->\n```text\n```\n'
  assert.equal(replaceBlock(empty, 'e', 'filled'), '<!-- capture: e -->\n```text\nfilled\n```\n')
})

test('replaceBlock on an unknown id is an error', () => {
  assert.throws(() => replaceBlock(DOC, 'missing', 'x'), /no marker 'missing'/)
})

test('replaceBlock refuses content that holds a line closing the fence', () => {
  assert.throws(() => replaceBlock(DOC, 'one', 'a\n```\nb'), /would close the fence/)
})

test('blockRegion returns the fences and the content, which is what the languages must share', () => {
  assert.deepEqual(blockRegion(DOC, 'one'), ['```text', 'old line', '```'])
  assert.equal(blockRegion(DOC, 'nope'), null)
})
