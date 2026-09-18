import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { test } from 'node:test'
import { fileURLToPath } from 'node:url'

const CLI = join(dirname(fileURLToPath(import.meta.url)), 'check-table-width.mjs')

function run(guideRoot) {
  return spawnSync(process.execPath, [CLI, guideRoot], { encoding: 'utf8', cwd: guideRoot })
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

test('a missing guide root is an error, not a silent pass', () => {
  const missing = join(tmpdir(), 'struo-table-guard-does-not-exist')
  const result = spawnSync(process.execPath, [CLI, missing], { encoding: 'utf8', cwd: tmpdir() })
  assert.equal(result.status, 1)
  assert.match(result.stderr, /not a directory/)
})

test('a root outside the working directory is rejected, not read', () => {
  const cwd = mkdtempSync(join(tmpdir(), 'struo-table-guard-cwd-'))
  const outside = mkdtempSync(join(tmpdir(), 'struo-table-guard-outside-'))
  try {
    const result = spawnSync(process.execPath, [CLI, outside], { encoding: 'utf8', cwd })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /outside the working directory/)
  } finally {
    rmSync(cwd, { recursive: true, force: true })
    rmSync(outside, { recursive: true, force: true })
  }
})
