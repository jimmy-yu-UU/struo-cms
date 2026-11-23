import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { test } from 'node:test'
import { fileURLToPath } from 'node:url'

const CLI = join(dirname(fileURLToPath(import.meta.url)), 'capture.mjs')

function run(args, cwd, env = {}) {
  return spawnSync(process.execPath, [CLI, ...args], {
    encoding: 'utf8',
    cwd,
    env: { PATH: process.env.PATH, ...env },
  })
}

function makeGuide(zh, en) {
  const root = mkdtempSync(join(tmpdir(), 'struo-cli-'))
  mkdirSync(join(root, 'zh-TW'))
  mkdirSync(join(root, 'en'))
  writeFileSync(join(root, 'zh-TW', '99-x.md'), zh)
  writeFileSync(join(root, 'en', '99-x.md'), en)
  return root
}

test('--check exits 0 on a guide without markers and says what it checked', () => {
  const root = makeGuide('# a\n', '# a\n')
  try {
    const result = run(['--check', '--guide-dir', root], root)
    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /0 marked blocks in 2 files/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('--check exits 1 and prints the id and file of a block that differs between languages', () => {
  const block = (body) => `<!-- capture: q -->\n\`\`\`text\n${body}\n\`\`\`\n`
  const root = makeGuide(block('a'), block('b'))
  try {
    const result = run(['--check', '--guide-dir', root], root)
    assert.equal(result.status, 1)
    assert.match(result.stderr, /q: .*differs.*zh-TW/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('--check on the real guide passes', () => {
  const result = run(['--check'], join(dirname(CLI), '..', '..'))
  assert.equal(result.status, 0, result.stderr)
})

test('a capture run without a base URL exits 1 with a message', () => {
  const root = makeGuide('# a\n', '# a\n')
  try {
    const result = run([], root)
    assert.equal(result.status, 1)
    assert.match(result.stderr, /base URL/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('a capture run without the admin password exits 1 and names the variable', () => {
  const root = makeGuide('# a\n', '# a\n')
  try {
    const result = run(['--base-url', 'http://127.0.0.1:9'], root)
    assert.equal(result.status, 1)
    assert.match(result.stderr, /STRUO_CAPTURE_ADMIN_PASSWORD/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('an unreachable host exits 1', () => {
  const result = run(['--base-url', 'http://127.0.0.1:9', '--chapters', '10'], join(dirname(CLI), '..', '..'), {
    STRUO_CAPTURE_ADMIN_PASSWORD: 'x',
  })
  assert.equal(result.status, 1)
  assert.match(result.stderr, /unreachable/)
})

test('an unknown chapter number exits 1', () => {
  const result = run(['--base-url', 'http://127.0.0.1:9', '--chapters', '99'], join(dirname(CLI), '..', '..'), {
    STRUO_CAPTURE_ADMIN_PASSWORD: 'x',
  })
  assert.equal(result.status, 1)
  assert.match(result.stderr, /no scenario for chapter 99/)
})
