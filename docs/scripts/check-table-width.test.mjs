import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { test } from 'node:test'
import { fileURLToPath } from 'node:url'

const CLI = join(dirname(fileURLToPath(import.meta.url)), 'check-table-width.mjs')

function run(guideRoot) {
  return spawnSync(process.execPath, [CLI, guideRoot], { encoding: 'utf8' })
}

function makeGuide(files) {
  const root = mkdtempSync(join(tmpdir(), 'struo-table-guard-'))
  for (const [relative, content] of Object.entries(files)) {
    const path = join(root, relative)
    mkdirSync(dirname(path), { recursive: true })
    writeFileSync(path, content)
  }
  return root
}

test('exits 0 and reports the file count when every table fits', () => {
  const root = makeGuide({
    'en/01-a.md': '# A\n\n| k | v |\n|---|---|\n| a | b |\n',
    'zh-TW/01-a.md': '# 甲\n\n| 鍵 | 值 |\n|---|---|\n| 甲 | 乙 |\n',
    'index.md': '# Root\n',
  })
  try {
    const result = run(root)
    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /checked 3 markdown files/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('exits 1 and names locale, file, line and cause for each violation', () => {
  const root = makeGuide({
    'en/01-a.md': '# A\n\n| a | b | c | d | e |\n|---|---|---|---|---|\n',
    'zh-TW/01-a.md': `# 甲\n\n| 鍵 |\n|---|\n| ${'字'.repeat(31)} |\n`,
  })
  try {
    const result = run(root)
    assert.equal(result.status, 1)
    assert.match(result.stderr, /^en\/01-a\.md:3: 5 columns \(max 4\)$/m)
    assert.match(result.stderr, /^zh-TW\/01-a\.md:5: cell width 62 > 60/m)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('the legacy allowlist exempts the sixteen old chapter filenames in every locale', () => {
  const wide = '# Old\n\n| a | b | c | d | e |\n|---|---|---|---|---|\n'
  const root = makeGuide({
    'en/09-rest-api.md': wide,
    'zh-TW/09-rest-api.md': wide,
    'en/02-new.md': '# New\n\n| a | b |\n|---|---|\n',
    'zh-TW/02-new.md': '# 新\n\n| a | b |\n|---|---|\n',
  })
  try {
    const result = run(root)
    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /2 legacy chapter files skipped/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('a missing guide root is an error, not a silent pass', () => {
  const result = run(join(tmpdir(), 'struo-table-guard-does-not-exist'))
  assert.equal(result.status, 1)
  assert.match(result.stderr, /not a directory/)
})
