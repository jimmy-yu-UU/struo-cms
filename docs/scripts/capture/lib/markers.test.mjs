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

test('the new lines take the file\'s first line ending; every other line keeps its own', () => {
  const mixed = '# t\r\n\n<!-- capture: a -->\n```text\nold\n```\n'
  assert.equal(replaceBlock(mixed, 'a', 'x\ny'), '# t\r\n\n<!-- capture: a -->\n```text\nx\r\ny\r\n```\n')
  const lf = 'a\n<!-- capture: a -->\r\n```text\r\nold\r\n```\r\n'
  assert.equal(replaceBlock(lf, 'a', 'x'), 'a\n<!-- capture: a -->\r\n```text\r\nx\n```\r\n')
})

test('a file with no line ending at all uses LF', () => {
  assert.equal(replaceBlock('<!-- capture: z -->\n```text\n```', 'z', 'new'), '<!-- capture: z -->\n```text\nnew\n```')
})

test('replaceBlock refuses captured text that holds a carriage return', () => {
  assert.throws(() => replaceBlock(DOC, 'one', 'a\r\nb'), /carriage return/)
  assert.throws(() => replaceBlock(DOC, 'one', 'a\rb'), /carriage return/)
})

test('a line that contains a capture comment but is not the exact marker form is an error', () => {
  const near = [
    ['trailing space', '<!-- capture: a --> \n```text\nx\n```\n'],
    ['no space after the opener', '<!--capture: a-->\n```text\nx\n```\n'],
    ['indented', '  <!-- capture: a -->\n  ```text\n  x\n  ```\n'],
    ['mid-line in prose', 'see <!-- capture: a --> here\n\n```text\nx\n```\n'],
    ['uppercase', '<!-- CAPTURE: a -->\n```text\nx\n```\n'],
    ['missing colon', '<!-- capture a -->\n```text\nx\n```\n'],
  ]
  for (const [name, text] of near) {
    assert.throws(() => findBlocks(text, { file: 'a.md' }), /a\.md:\d+: .*near-miss capture marker/, name)
  }
})

test('the near-miss rule reports the line of the offending comment', () => {
  assert.throws(() => findBlocks('one\ntwo\n<!--capture: a-->\n', { file: 'f.md' }), /f\.md:3: /)
})

test('capture comments inside a fenced block are content and never near-misses', () => {
  const text = '```md\n<!--capture: a-->\n  <!-- capture: b -->\n```\n'
  assert.deepEqual(findBlocks(text), [])
})

test('an inline-code mention of the marker form in prose is not a near-miss', () => {
  assert.deepEqual(findBlocks('Put `<!-- capture: <id> -->` above the fence.\n'), [])
  assert.deepEqual(findBlocks('Two: `<!--capture: a-->` and ``<!-- capture: b -->`` here.\n'), [])
})

test('a near-miss beside an inline-code span is still an error', () => {
  assert.throws(() => findBlocks('`ok` and <!--capture: a-->\n', { file: 'f.md' }), /f\.md:1: .*near-miss/)
})

test('indented code after a blank line is exempt, but a marker indented inside a list is not', () => {
  assert.deepEqual(findBlocks('para\n\n    <!-- capture: a -->\n'), [])
  assert.deepEqual(findBlocks('para\n\n\t<!--capture: a-->\n'), [])
  assert.throws(() => findBlocks('- item\n\n  <!-- capture: a -->\n  ```text\n  x\n  ```\n'), /near-miss/)
})
