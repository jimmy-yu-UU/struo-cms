import assert from 'node:assert/strict'
import { test } from 'node:test'
import { LIMITS, cellText, checkTables, displayWidth } from './markdown-tables.mjs'

test('displayWidth counts CJK as 2 and everything else as 1', () => {
  assert.equal(displayWidth('abc'), 3)
  assert.equal(displayWidth('欄位'), 4)
  assert.equal(displayWidth('a欄b'), 4)
  assert.equal(displayWidth('，。'), 4) // fullwidth punctuation is CJK-width too
  assert.equal(displayWidth(''), 0)
})

test('cellText strips inline code, links and emphasis before measuring', () => {
  assert.equal(cellText(' `Database:DbType` '), 'Database:DbType')
  assert.equal(cellText('[ch. 4](04-configuration.md)'), 'ch. 4')
  assert.equal(cellText('**bold** and __also__'), 'bold and also')
  assert.equal(cellText('a \\| b'), 'a \\| b') // escaped pipe is content, left alone
})

test('LIMITS carries the spec values', () => {
  assert.deepEqual(LIMITS, { maxColumns: 4, maxCellWidth: 60 })
})

test('a compliant table produces no violations', () => {
  const md = [
    '# Title',
    '',
    '| Key | Type | Default |',
    '|---|---|---|',
    '| `Database:DbType` | string | `PostgreSQL` |',
    '',
  ].join('\n')
  assert.deepEqual(checkTables(md, LIMITS), [])
})

test('a row with more than maxColumns cells is reported once, on its own line', () => {
  const md = [
    '| a | b | c | d | e |',
    '|---|---|---|---|---|',
    '| 1 | 2 | 3 | 4 | 5 |',
  ].join('\n')
  const violations = checkTables(md, LIMITS)
  assert.deepEqual(
    violations.map((v) => [v.line, v.kind]),
    [
      [1, 'columns'],
      [3, 'columns'],
    ],
  )
  assert.match(violations[0].detail, /5 columns/)
})

test('a cell wider than maxCellWidth is reported with its text', () => {
  const long = 'x'.repeat(61)
  const md = ['| a | b |', '|---|---|', `| ${long} | ok |`].join('\n')
  const violations = checkTables(md, LIMITS)
  assert.equal(violations.length, 1)
  assert.equal(violations[0].line, 3)
  assert.equal(violations[0].kind, 'width')
  assert.match(violations[0].detail, /61 > 60/)
  assert.ok(violations[0].detail.includes(long))
})

test('width is measured on the cleaned text, so backticks and link targets do not count', () => {
  // 58 visible characters wrapped in backticks and a link: raw length is far over 60.
  const visible = 'y'.repeat(58)
  const md = ['| a |', '|---|', `| [\`${visible}\`](a-very-long-target-that-would-push-it-over.md) |`].join('\n')
  assert.deepEqual(checkTables(md, LIMITS), [])
})

test('CJK counts double: 31 CJK characters exceed 60', () => {
  const md = ['| a |', '|---|', `| ${'字'.repeat(31)} |`].join('\n')
  assert.equal(checkTables(md, LIMITS).length, 1)
  const ok = ['| a |', '|---|', `| ${'字'.repeat(30)} |`].join('\n')
  assert.deepEqual(checkTables(ok, LIMITS), [])
})

test('tables inside fenced code are ignored, including ~~~ fences and longer closers', () => {
  const wide = '| a | b | c | d | e |'
  const md = [
    '```md',
    wide,
    '|---|---|---|---|---|',
    '```',
    '~~~~',
    wide,
    '~~~~~',
    '',
    '| ok |',
    '|---|',
    '| fine |',
  ].join('\n')
  assert.deepEqual(checkTables(md, LIMITS), [])
})

test('the delimiter row is never measured even when it is long', () => {
  const md = ['| a | b |', `|${'-'.repeat(80)}|${'-'.repeat(80)}|`, '| 1 | 2 |'].join('\n')
  assert.deepEqual(checkTables(md, LIMITS), [])
})

test('line numbers are 1-based and survive CRLF input', () => {
  const md = ['intro', '', '| a | b | c | d | e |', '|---|---|---|---|---|'].join('\r\n')
  const violations = checkTables(md, LIMITS)
  assert.equal(violations[0].line, 3)
})
