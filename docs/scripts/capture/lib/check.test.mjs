import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { test } from 'node:test'
import { checkGuide, declaredByFile } from './check.mjs'

const block = (id, body = 'x\nHTTP_STATUS:200') => `<!-- capture: ${id} -->\n\`\`\`text\n${body}\n\`\`\`\n`

function guide(zh, en, extra = {}) {
  const root = mkdtempSync(join(tmpdir(), 'struo-check-'))
  mkdirSync(join(root, 'zh-TW'))
  mkdirSync(join(root, 'en'))
  if (zh !== null) writeFileSync(join(root, 'zh-TW', '01-a.md'), zh)
  if (en !== null) writeFileSync(join(root, 'en', '01-a.md'), en)
  for (const [path, text] of Object.entries(extra)) writeFileSync(join(root, path), text)
  return root
}

const cleanup = (root) => rmSync(root, { recursive: true, force: true })
const capture = (id, expect = 200, file) => ({ capture: id, file, method: 'GET', path: '/x', expect })
const group = (steps, file = '01-a.md') => ({ name: 'g', file, steps })

test('chapters without markers pass', () => {
  const root = guide('# a\n', '# a\n')
  try {
    assert.deepEqual(checkGuide({ guideDir: root, groups: [] }), { problems: [], blocks: 0, files: 2 })
  } finally {
    cleanup(root)
  }
})

test('identical registered blocks pass and are counted', () => {
  const root = guide(`中\n${block('a')}`, `en\n${block('a')}`)
  try {
    const result = checkGuide({ guideDir: root, groups: [group([capture('a')])] })
    assert.deepEqual(result.problems, [])
    assert.equal(result.blocks, 1)
  } finally {
    cleanup(root)
  }
})

test('a block that differs between the languages is reported with its id and both files', () => {
  const root = guide(block('a', 'one'), block('a', 'two'))
  try {
    const { problems } = checkGuide({ guideDir: root, groups: [group([capture('a')])] })
    assert.match(problems.join('\n'), /a: block differs between zh-TW[\\/]01-a\.md and en[\\/]01-a\.md/)
  } finally {
    cleanup(root)
  }
})

test('a marker that exists in one language only is reported', () => {
  const root = guide(block('a') + block('b'), block('a'))
  try {
    const { problems } = checkGuide({ guideDir: root, groups: [group([capture('a'), capture('b')])] })
    assert.match(problems.join('\n'), /b: marker only in zh-TW[\\/]01-a\.md/)
  } finally {
    cleanup(root)
  }
})

test('a well-formed marker with no registered capture fails, in a file with no scenario too', () => {
  const root = guide(block('made-up'), block('made-up'))
  try {
    const { problems } = checkGuide({ guideDir: root, groups: [] })
    assert.match(problems.join('\n'), /made-up: zh-TW[\\/]01-a\.md holds a marker with no capture/)
    assert.match(problems.join('\n'), /made-up: en[\\/]01-a\.md holds a marker with no capture/)
  } finally {
    cleanup(root)
  }
})

test('a capture whose marker is missing is reported for each language', () => {
  const root = guide('# z\n', '# e\n')
  try {
    const text = checkGuide({ guideDir: root, groups: [group([capture('missing')])] }).problems.join('\n')
    assert.match(text, /missing: no marker in zh-TW[\\/]01-a\.md/)
    assert.match(text, /missing: no marker in en[\\/]01-a\.md/)
  } finally {
    cleanup(root)
  }
})

test('a chapter file that exists in one language only is an error', () => {
  const root = guide('# only zh\n', null)
  try {
    assert.match(checkGuide({ guideDir: root, groups: [] }).problems.join('\n'), /01-a\.md: exists only in zh-TW/)
  } finally {
    cleanup(root)
  }
})

test('a near-miss marker fails with file and line', () => {
  const root = guide('# a\n\n<!--capture: a-->\n```text\nx\n```\n', '# a\n')
  try {
    assert.match(checkGuide({ guideDir: root, groups: [] }).problems.join('\n'), /zh-TW[\\/]01-a\.md:3: .*near-miss capture marker/)
  } finally {
    cleanup(root)
  }
})

test('a marker with no fence below it is reported with file and line', () => {
  const root = guide('<!-- capture: a -->\n\nprose\n', '# a\n')
  try {
    assert.match(checkGuide({ guideDir: root, groups: [] }).problems[0], /zh-TW[\\/]01-a\.md:1: marker 'a' is not directly above a fence/)
  } finally {
    cleanup(root)
  }
})

test('the last HTTP_STATUS line of a block must equal the declared status of its last exchange', () => {
  const root = guide(block('a', 'x\nHTTP_STATUS:400'), block('a', 'x\nHTTP_STATUS:400'))
  try {
    const { problems } = checkGuide({ guideDir: root, groups: [group([capture('a', 200)])] })
    assert.match(problems.join('\n'), /a: .*ends with HTTP_STATUS:400 but the scenario declares 200/)
  } finally {
    cleanup(root)
  }
})

test('captures of one file are collected across groups that are not adjacent', () => {
  const groups = [
    group([capture('a'), capture('b')]),
    { name: 'other', file: '02-b.md', steps: [capture('c')] },
    group([capture('d')]),
  ]
  const declared = declaredByFile(groups)
  assert.deepEqual([...declared.get('01-a.md').keys()], ['a', 'b', 'd'])
  assert.deepEqual([...declared.get('02-b.md').keys()], ['c'])
})

test('a capture step can name its own file, and a capture with no file anywhere is an error', () => {
  const declared = declaredByFile([{ name: 'g', steps: [capture('a', 200, '03-c.md')] }])
  assert.deepEqual([...declared.get('03-c.md').keys()], ['a'])
  assert.throws(() => declaredByFile([{ name: 'g', steps: [capture('a')] }]), /a: .*names no chapter file/)
})

test('one capture id in two files is an error', () => {
  assert.throws(
    () => declaredByFile([group([capture('a')]), group([capture('a')], '02-b.md')]),
    /a: .*01-a\.md and 02-b\.md/,
  )
})

test('files outside the NN-name.md pattern are ignored', () => {
  const root = guide('# a\n', '# a\n', { 'zh-TW/index.md': '<!-- capture: z -->\n' })
  try {
    assert.deepEqual(checkGuide({ guideDir: root, groups: [] }).problems, [])
  } finally {
    cleanup(root)
  }
})
