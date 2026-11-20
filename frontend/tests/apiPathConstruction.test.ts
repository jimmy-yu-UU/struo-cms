// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'

const SRC = fileURLToPath(new URL('../src/', import.meta.url))
const API_DIR = join(SRC, 'api')

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    return statSync(full).isDirectory() ? walk(full) : [full]
  })
}

const rel = (file: string): string => relative(SRC, file).replaceAll('\\', '/')
const isTest = (file: string): boolean => /\.(test|spec)\.ts$/.test(file)

// Comments are not code: block comments, HTML comments and the tail of a line comment (a `//` that
// follows whitespace or starts the line, so the `//` of a URL inside a string survives).
function stripComments(text: string): string {
  return text
    .replaceAll(/\/\*[\s\S]*?\*\//g, (m) => m.replaceAll(/[^\n]/g, ' '))
    .replaceAll(/<!--[\s\S]*?-->/g, (m) => m.replaceAll(/[^\n]/g, ' '))
    .split('\n')
    .map((line) => line.replace(/(^|\s)\/\/.*$/, '$1'))
    .join('\n')
}

type Source = { file: string; code: string }

function readSources(files: string[]): Source[] {
  return files.map((file) => ({ file: rel(file), code: stripComments(readFileSync(file, 'utf8')) }))
}

const apiSources = readSources(walk(API_DIR).filter((f) => f.endsWith('.ts') && !isTest(f)))
const appSources = readSources(
  walk(SRC).filter((f) => /\.(ts|vue)$/.test(f) && !isTest(f) && !f.endsWith('.d.ts')))

function offending(sources: Source[], pattern: RegExp, allowed: (s: Source, line: string) => boolean = () => false): string[] {
  return sources.flatMap(({ file, code }) =>
    code.split('\n')
      .map((line, i) => ({ line, n: i + 1 }))
      .filter(({ line }) => pattern.test(line) && !allowed({ file, code }, line))
      .map(({ n }) => `${file}:${n}`))
}

// A string literal next to a `+`: the shape of a hand-built path or query.
const LITERAL_CONCAT = /(['"`])[^'"`\n]*\1\s*\+|\+\s*(['"`])/

describe('API paths are built in one place', () => {
  const modules = apiSources.filter((s) => s.file !== 'api/apiPath.ts' && s.file !== 'api/apiClient.ts')

  it('finds the API modules, subfolders included', () => {
    expect(modules.map((m) => m.file)).toContain('api/itemsApi.ts')
    expect(modules.map((m) => m.file)).toContain('api/rbacApi.ts')
  })

  it('no API module interpolates into a string', () => {
    expect(offending(modules, /\$\{/)).toEqual([])
  })

  it('no API module joins a string literal and a value with +', () => {
    expect(offending(modules, LITERAL_CONCAT)).toEqual([])
  })

  it('apiClient.ts holds the single baseUrl + path concatenation', () => {
    const client = apiSources.find((s) => s.file === 'api/apiClient.ts')!
    const lines = client.code.split('\n').filter((line) => line.includes('${'))
    expect(lines.map((line) => line.trim())).toEqual(['return `${this.baseUrl}${path}`'])
  })

  it('does not trip on interpolation that only appears in a comment', () => {
    const stripped = stripComments('// a ${b} c\nconst x = 1 // ${d}\n/* ${e} */\nconst y = "http://x"')
    expect(stripped).not.toContain('${')
    expect(stripped).toContain('"http://x"')
  })
})

describe('every request path goes through apiPath', () => {
  const CALL = /apiClient\.(?:get|getRaw|post|postForm|put|delete)(?:<[^(\n]*>)?\(\s*([^\s][^\n]*)/g

  function firstArguments(code: string): string[] {
    return [...code.matchAll(CALL)].map((m) => m[1])
  }
  const acceptable = (arg: string): boolean => /^'[^'\n$]*'\s*(?:[,)]|$)/.test(arg) || /^(?:apiPath|withQuery)\(/.test(arg)

  it('the first argument of each apiClient call is a plain string literal, apiPath(...) or withQuery(...)', () => {
    const offenders = appSources.flatMap(({ file, code }) =>
      firstArguments(code).filter((arg) => !acceptable(arg)).map((arg) => `${file}: ${arg}`))
    expect(offenders).toEqual([])
  })

  it('the check sees the calls it is meant to see', () => {
    const counts = appSources.map((s) => firstArguments(s.code).length)
    expect(counts.reduce((a, b) => a + b, 0)).toBeGreaterThan(20)
  })

  it('rejects an interpolated, concatenated or variable path', () => {
    expect(acceptable('`/items/${id}`')).toBe(false)
    expect(acceptable("'/items/' + id")).toBe(false)
    expect(acceptable('path')).toBe(false)
    expect(acceptable("'/auth/login', { email })")).toBe(true)
    expect(acceptable("apiPath('items', id))")).toBe(true)
  })
})

describe('the base URL and fetch belong to apiClient.ts', () => {
  // `fetch` as the name of a callback parameter is not the network call.
  const LOCAL_FETCH_CALLS: Record<string, string> = { 'lib/guardedLoad.ts': 'const result = await fetch()' }
  const FETCH_CALL = /(?<![\w$.])fetch\s*\(|\b(?:window|globalThis|self)\.fetch\b/

  it('apiBaseUrl is named nowhere else', () => {
    const others = appSources.filter((s) => s.file !== 'api/apiClient.ts')
    expect(offending(others, /\bapiBaseUrl\b/)).toEqual([])
  })

  it('fetch( is called nowhere else', () => {
    const others = appSources.filter((s) => s.file !== 'api/apiClient.ts')
    expect(offending(others, FETCH_CALL, (s, line) => LOCAL_FETCH_CALLS[s.file] === line.trim())).toEqual([])
  })

  it('the listed exception is still a callback named fetch', () => {
    const source = appSources.find((s) => s.file === 'lib/guardedLoad.ts')!
    expect(source.code).toContain('fetch: () => Promise<T>')
    expect(source.code).toContain(LOCAL_FETCH_CALLS['lib/guardedLoad.ts'])
  })
})
