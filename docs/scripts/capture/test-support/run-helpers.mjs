// Helpers for the runner tests that run a scenario against a stub host in a temporary guide.
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { runCapture } from '../lib/runner.mjs'
import { FIXTURE_PASSWORD } from './fixtures.mjs'
import { startStub } from './stub-server.mjs'

export const DEMO = '01-demo.md'
export const OTHER = '02-other.md'
const BLOCK = (id) => `<!-- capture: ${id} -->\n\`\`\`text\nold\n\`\`\`\n`
const noGuard = async () => {}
export const OK = () => ({ status: 200, body: '{"ok":true}' })

export function makeRoot(ids) {
  const root = mkdtempSync(join(tmpdir(), 'struo-steps-'))
  const text = ids.map(BLOCK).join('\n')
  for (const dir of ['zh-TW', 'en']) {
    mkdirSync(join(root, dir))
    for (const name of [DEMO, OTHER]) writeFileSync(join(root, dir, name), text)
  }
  mkdirSync(join(root, 'fixtures'))
  writeFileSync(join(root, 'fixtures', 'note.txt'), 'twelve bytes')
  return root
}

export async function run(root, steps, over = {}, handler = OK) {
  const stub = await startStub(handler)
  try {
    const result = await runCapture({
      baseUrl: stub.baseUrl,
      vars: { adminPassword: FIXTURE_PASSWORD, lockEmail: 'probe@example.com' },
      groups: [{ name: 'g', file: DEMO, steps }],
      guideDir: root,
      fixturesDir: join(root, 'fixtures'),
      write: false,
      guard: noGuard,
      ...over,
    })
    return { ...result, stub }
  } catch (error) {
    error.stub = stub
    throw error
  } finally {
    await stub.close()
  }
}

export async function inRoot(ids, fn) {
  const root = makeRoot(ids)
  try {
    return await fn(root)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
}
