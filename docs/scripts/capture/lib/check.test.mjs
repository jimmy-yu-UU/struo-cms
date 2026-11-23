import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { test } from 'node:test'
import { checkGuide } from './check.mjs'

const block = (id, body = 'x\nHTTP_STATUS:200') => `<!-- capture: ${id} -->\n\`\`\`text\n${body}\n\`\`\`\n`

function guide(zh, en, extra = {}) {
  const root = mkdtempSync(join(tmpdir(), 'struo-check-'))
  mkdirSync(join(root, 'zh-TW'))
  mkdirSync(join(root, 'en'))
  writeFileSync(join(root, 'zh-TW', '01-a.md'), zh)
  writeFileSync(join(root, 'en', '01-a.md'), en)
  for (const [path, text] of Object.entries(extra)) writeFileSync(join(root, path), text)
  return root
}

const cleanup = (root) => rmSync(root, { recursive: true, force: true })

test('chapters without markers pass', () => {
  const root = guide('# a\n', '# a\n')
  try {
    assert.deepEqual(checkGuide({ guideDir: root, chapters: [] }), { problems: [], blocks: 0, files: 2 })
  } finally {
    cleanup(root)
  }
})

test('identical marked blocks pass and are counted', () => {
  const root = guide(`中\n${block('a')}`, `en\n${block('a')}`)
  try {
    const result = checkGuide({ guideDir: root, chapters: [] })
    assert.deepEqual(result.problems, [])
    assert.equal(result.blocks, 1)
  } finally {
    cleanup(root)
  }
})

test('a block that differs between the languages is reported with its id and both files', () => {
  const root = guide(block('a', 'one'), block('a', 'two'))
  try {
    const { problems } = checkGuide({ guideDir: root, chapters: [] })
    assert.equal(problems.length, 1)
    assert.match(problems[0], /a: .*differs.*zh-TW[\\/]01-a\.md.*en[\\/]01-a\.md/)
  } finally {
    cleanup(root)
  }
})

test('a marker that exists in one language only is reported', () => {
  const root = guide(block('a') + block('b'), block('a'))
  try {
    const { problems } = checkGuide({ guideDir: root, chapters: [] })
    assert.match(problems.join('\n'), /b: .*only in zh-TW[\\/]01-a\.md/)
  } finally {
    cleanup(root)
  }
})

test('a marker with no fence below it is reported with file and line', () => {
  const root = guide('<!-- capture: a -->\n\nprose\n', '# a\n')
  try {
    assert.match(checkGuide({ guideDir: root, chapters: [] }).problems[0], /zh-TW[\\/]01-a\.md:1: marker 'a' is not directly above a fence/)
  } finally {
    cleanup(root)
  }
})

test('a registered chapter must have a marker for every capture and a capture for every marker', () => {
  const root = guide(block('a') + block('stray'), block('a') + block('stray'))
  const steps = [
    { capture: 'a', method: 'GET', path: '/x', expect: 200 },
    { capture: 'missing', method: 'GET', path: '/y', expect: 200 },
  ]
  try {
    const text = checkGuide({ guideDir: root, chapters: [{ number: '01', file: '01-a.md', steps }] }).problems.join('\n')
    assert.match(text, /missing: .*no marker.*zh-TW[\\/]01-a\.md/)
    assert.match(text, /missing: .*no marker.*en[\\/]01-a\.md/)
    assert.match(text, /stray: .*no capture/)
  } finally {
    cleanup(root)
  }
})

test('the last HTTP_STATUS line of a block must equal the declared status of its last exchange', () => {
  const root = guide(block('a', 'x\nHTTP_STATUS:400'), block('a', 'x\nHTTP_STATUS:400'))
  const steps = [{ capture: 'a', method: 'GET', path: '/x', expect: 200 }]
  try {
    const { problems } = checkGuide({ guideDir: root, chapters: [{ number: '01', file: '01-a.md', steps }] })
    assert.match(problems.join('\n'), /a: .*HTTP_STATUS:400.*declares 200/)
  } finally {
    cleanup(root)
  }
})

test('files outside the NN-name.md pattern are ignored', () => {
  const root = guide('# a\n', '# a\n', { 'zh-TW/index.md': '<!-- capture: z -->\n' })
  try {
    assert.deepEqual(checkGuide({ guideDir: root, chapters: [] }).problems, [])
  } finally {
    cleanup(root)
  }
})
