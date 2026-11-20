// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const API_DIR = fileURLToPath(new URL('../src/api/', import.meta.url))

const sources = readdirSync(API_DIR)
  .filter((f) => f.endsWith('.ts') && !f.endsWith('.test.ts'))
  .map((f) => ({ file: f, text: readFileSync(join(API_DIR, f), 'utf8') }))

// A string literal next to a `+`: the shape of a hand-built path or query.
const LITERAL_CONCAT = /(['"`])[^'"`\n]*\1\s*\+|\+\s*(['"`])/

function codeLines(text: string): Array<{ n: number; line: string }> {
  return text.split('\n')
    .map((line, i) => ({ n: i + 1, line }))
    .filter(({ line }) => !line.trimStart().startsWith('//'))
}

describe('API paths are built in one place', () => {
  const modules = sources.filter((s) => s.file !== 'apiPath.ts' && s.file !== 'apiClient.ts')

  it('finds the API modules', () => {
    expect(modules.map((m) => m.file)).toContain('itemsApi.ts')
    expect(modules.map((m) => m.file)).toContain('rbacApi.ts')
  })

  it('no API module interpolates into a string', () => {
    const offenders = modules.flatMap((m) =>
      codeLines(m.text).filter(({ line }) => line.includes('${')).map(({ n }) => `${m.file}:${n}`))
    expect(offenders).toEqual([])
  })

  it('no API module joins a string literal and a value with +', () => {
    const offenders = modules.flatMap((m) =>
      codeLines(m.text).filter(({ line }) => LITERAL_CONCAT.test(line)).map(({ n }) => `${m.file}:${n}`))
    expect(offenders).toEqual([])
  })

  it('apiClient.ts holds the single baseUrl + path concatenation', () => {
    const client = sources.find((s) => s.file === 'apiClient.ts')!
    const lines = codeLines(client.text).filter(({ line }) => line.includes('${'))
    expect(lines.map(({ line }) => line.trim())).toEqual([
      'return `${this.baseUrl}${path}`',
    ])
  })
})
