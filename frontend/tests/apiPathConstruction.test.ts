// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

const SRC = fileURLToPath(new URL('../src/', import.meta.url))

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    return statSync(full).isDirectory() ? walk(full) : [full]
  })
}

const rel = (file: string): string => relative(SRC, file).replaceAll('\\', '/')
const isTest = (file: string): boolean => /\.(test|spec)\.ts$/.test(file)

// ---- tokens -------------------------------------------------------------------------------------
// TypeScript's own scanner, so a string such as 'image/*' or a template literal is never mistaken for
// a comment. The scanner knows nothing of the parser's context, so template continuations and regular
// expression literals are re-scanned where the grammar calls for it.

type Token = { kind: ts.SyntaxKind; text: string; line: number }

// Script text of a source file: a .ts file as it is, a .vue file the content of its <script> blocks.
function scriptText(file: string, text: string): string {
  if (!file.endsWith('.vue')) return text
  return [...text.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/g)].map((m) => m[1]).join('\n')
}

const REGEX_CAN_START_AFTER = new Set<ts.SyntaxKind | undefined>([
  undefined, ts.SyntaxKind.OpenParenToken, ts.SyntaxKind.CommaToken, ts.SyntaxKind.EqualsToken,
  ts.SyntaxKind.ColonToken, ts.SyntaxKind.OpenBracketToken, ts.SyntaxKind.ExclamationToken,
  ts.SyntaxKind.AmpersandAmpersandToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionToken,
  ts.SyntaxKind.OpenBraceToken, ts.SyntaxKind.CloseBraceToken, ts.SyntaxKind.SemicolonToken,
  ts.SyntaxKind.ReturnKeyword, ts.SyntaxKind.EqualsGreaterThanToken,
])

function tokenize(source: string): Token[] {
  const scanner = ts.createScanner(ts.ScriptTarget.Latest, false, ts.LanguageVariant.Standard, source)
  const tokens: Token[] = []
  const templateDepths: number[] = []
  let braceDepth = 0
  let previous: ts.SyntaxKind | undefined
  let line = 1
  for (let kind = scanner.scan(); kind !== ts.SyntaxKind.EndOfFileToken; kind = scanner.scan()) {
    if ((kind === ts.SyntaxKind.SlashToken || kind === ts.SyntaxKind.SlashEqualsToken)
      && REGEX_CAN_START_AFTER.has(previous)) {
      kind = scanner.reScanSlashToken()
    }
    if (kind === ts.SyntaxKind.TemplateHead) templateDepths.push(braceDepth)
    else if (kind === ts.SyntaxKind.OpenBraceToken) braceDepth += 1
    else if (kind === ts.SyntaxKind.CloseBraceToken) {
      if (templateDepths.length > 0 && templateDepths.at(-1) === braceDepth) {
        kind = scanner.reScanTemplateToken(false)
        if (kind === ts.SyntaxKind.TemplateTail) templateDepths.pop()
      } else braceDepth -= 1
    }
    const text = scanner.getTokenText()
    tokens.push({ kind, text, line })
    line += text.split('\n').length - 1
    if (kind !== ts.SyntaxKind.WhitespaceTrivia && kind !== ts.SyntaxKind.NewLineTrivia
      && kind !== ts.SyntaxKind.SingleLineCommentTrivia && kind !== ts.SyntaxKind.MultiLineCommentTrivia) {
      previous = kind
    }
  }
  return tokens
}

const isTrivia = (t: Token): boolean =>
  t.kind === ts.SyntaxKind.WhitespaceTrivia || t.kind === ts.SyntaxKind.NewLineTrivia
  || t.kind === ts.SyntaxKind.SingleLineCommentTrivia || t.kind === ts.SyntaxKind.MultiLineCommentTrivia

const significant = (source: string): Token[] => tokenize(source).filter((t) => !isTrivia(t))

// The source with every comment blanked and its line breaks kept.
function withoutComments(source: string): string {
  return tokenize(source)
    .map((t) => (t.kind === ts.SyntaxKind.SingleLineCommentTrivia || t.kind === ts.SyntaxKind.MultiLineCommentTrivia
      ? t.text.replaceAll(/[^\n]/g, ' ')
      : t.text))
    .join('')
}

const isLiteral = (t: Token | undefined): boolean =>
  t !== undefined && (t.kind === ts.SyntaxKind.StringLiteral || t.kind === ts.SyntaxKind.NoSubstitutionTemplateLiteral
    || t.kind === ts.SyntaxKind.TemplateHead || t.kind === ts.SyntaxKind.TemplateMiddle
    || t.kind === ts.SyntaxKind.TemplateTail)

// ---- rules --------------------------------------------------------------------------------------

const REQUEST_METHODS = new Set(['get', 'getRaw', 'post', 'postForm', 'put', 'delete', 'urlFor'])
const OPENERS = new Set([ts.SyntaxKind.OpenParenToken, ts.SyntaxKind.OpenBracketToken, ts.SyntaxKind.OpenBraceToken])
const CLOSERS = new Set([ts.SyntaxKind.CloseParenToken, ts.SyntaxKind.CloseBracketToken, ts.SyntaxKind.CloseBraceToken])

type Call = { firstArgument: Token[] } | { unparsable: string }

// Every `apiClient.<method>` of the source: its first argument, or why it could not be read. A request
// method named without a call, or a call whose brackets do not balance, is unparsable.
function requestCalls(source: string): Call[] {
  const t = significant(source)
  const calls: Call[] = []
  for (let i = 0; i + 2 < t.length; i += 1) {
    if (t[i].text !== 'apiClient' || t[i + 1].kind !== ts.SyntaxKind.DotToken || !REQUEST_METHODS.has(t[i + 2].text)) continue
    let at = i + 3
    if (t[at]?.kind === ts.SyntaxKind.LessThanToken) {
      let angle = 0
      for (; at < t.length; at += 1) {
        if (t[at].kind === ts.SyntaxKind.LessThanToken) angle += 1
        if (t[at].kind === ts.SyntaxKind.GreaterThanToken) angle -= 1
        if (angle === 0) break
      }
      at += 1
    }
    if (t[at]?.kind !== ts.SyntaxKind.OpenParenToken) { calls.push({ unparsable: `apiClient.${t[i + 2].text} is not called` }); continue }
    const first: Token[] = []
    let depth = 0
    let closed = false
    for (let j = at + 1; j < t.length; j += 1) {
      if (OPENERS.has(t[j].kind)) depth += 1
      if (CLOSERS.has(t[j].kind)) {
        if (depth === 0) { closed = true; break }
        depth -= 1
      }
      if (depth === 0 && t[j].kind === ts.SyntaxKind.CommaToken) { closed = true; break }
      first.push(t[j])
    }
    calls.push(closed ? { firstArgument: first } : { unparsable: `apiClient.${t[i + 2].text}( is not closed` })
  }
  return calls
}

// The first argument is one string literal without interpolation (any quote style), or exactly one
// apiPath(...) or withQuery(...) call; nothing is interpolated into it or added to it.
function problemWithPath(arg: Token[]): string | null {
  if (arg.some((x) => x.kind === ts.SyntaxKind.TemplateHead)) return 'interpolates'
  if (arg.some((x) => x.kind === ts.SyntaxKind.PlusToken)) return 'concatenates'
  if (arg.length === 1 && (arg[0].kind === ts.SyntaxKind.StringLiteral || arg[0].kind === ts.SyntaxKind.NoSubstitutionTemplateLiteral)) return null
  const isHelperCall = arg.length >= 3 && (arg[0].text === 'apiPath' || arg[0].text === 'withQuery')
    && arg[1].kind === ts.SyntaxKind.OpenParenToken && arg.at(-1)!.kind === ts.SyntaxKind.CloseParenToken
  if (!isHelperCall) return 'is neither a string literal nor an apiPath(...) or withQuery(...) call'
  let depth = 0
  for (let k = 1; k < arg.length; k += 1) {
    if (arg[k].kind === ts.SyntaxKind.OpenParenToken) depth += 1
    if (arg[k].kind === ts.SyntaxKind.CloseParenToken) depth -= 1
    if (depth === 0 && k < arg.length - 1) return 'has more after the helper call'
  }
  return null
}

function pathProblems(source: string): string[] {
  return requestCalls(source).flatMap((call) => {
    if ('unparsable' in call) return [`cannot be read: ${call.unparsable}`]
    const problem = problemWithPath(call.firstArgument)
    return problem === null ? [] : [`${call.firstArgument.map((x) => x.text).join(' ')}: ${problem}`]
  })
}

// An API module neither interpolates into a string nor joins a string literal to a value with +.
function stringBuildingProblems(source: string): string[] {
  const t = significant(source)
  const out: string[] = []
  t.forEach((token, i) => {
    if (token.kind === ts.SyntaxKind.TemplateHead) out.push(`interpolation at token ${i}`)
    if (token.kind === ts.SyntaxKind.PlusToken && (isLiteral(t[i - 1]) || isLiteral(t[i + 1]))) out.push(`concatenation at token ${i}`)
  })
  return out
}

const identifierNamed = (source: string, name: string): boolean =>
  significant(source).some((x) => x.kind === ts.SyntaxKind.Identifier && x.text === name)

// The lines that call the global fetch: `fetch(`, or `window.fetch`, `globalThis.fetch`, `self.fetch`.
function fetchCallLines(source: string): string[] {
  const t = significant(source)
  const lines = withoutComments(source).split('\n')
  const found: string[] = []
  t.forEach((token, i) => {
    if (token.text !== 'fetch' || token.kind !== ts.SyntaxKind.Identifier) return
    const member = t[i - 1]?.kind === ts.SyntaxKind.DotToken
    const global = member && ['window', 'globalThis', 'self'].includes(t[i - 2]?.text ?? '')
    const called = t[i + 1]?.kind === ts.SyntaxKind.OpenParenToken
    if ((!member && called) || global) found.push(lines[token.line - 1].trim())
  })
  return found
}

// ---- sources ------------------------------------------------------------------------------------

type Source = { file: string; code: string }
const sources: Source[] = walk(SRC)
  .filter((f) => /\.(ts|vue)$/.test(f) && !isTest(f) && !f.endsWith('.d.ts'))
  .map((f) => ({ file: rel(f), code: scriptText(f, readFileSync(f, 'utf8')) }))
const apiModules = sources.filter((s) => s.file.startsWith('api/') && s.file !== 'api/apiPath.ts' && s.file !== 'api/apiClient.ts')

describe('API paths are built in one place', () => {
  it('finds the API modules, subfolders included', () => {
    expect(apiModules.map((m) => m.file)).toContain('api/itemsApi.ts')
    expect(apiModules.map((m) => m.file)).toContain('api/rbacApi.ts')
  })

  it('no API module interpolates into a string or joins a string literal with +', () => {
    expect(apiModules.flatMap((m) => stringBuildingProblems(m.code).map((p) => `${m.file}: ${p}`))).toEqual([])
  })

  it('apiClient.ts holds the single baseUrl + path concatenation', () => {
    const client = sources.find((s) => s.file === 'api/apiClient.ts')!
    const lines = withoutComments(client.code).split('\n').filter((line) => line.includes('${'))
    expect(lines.map((line) => line.trim())).toEqual(['return `${this.baseUrl}${path}`'])
  })

  it('stringBuildingProblems flags interpolation and concatenation, and accepts plain code', () => {
    expect(stringBuildingProblems('const a = `/x/${id}`')).toHaveLength(1)
    expect(stringBuildingProblems("const a = '/x/' + id")).toHaveLength(1)
    expect(stringBuildingProblems("const a = id + '/x'")).toHaveLength(1)
    expect(stringBuildingProblems("const a = 1 + 2; const b = '/x'; // ${c}")).toEqual([])
  })
})

describe('every request path goes through apiPath', () => {
  it('every apiClient request call in src has a plain string literal, apiPath(...) or withQuery(...) as its path', () => {
    expect(sources.flatMap((s) => pathProblems(s.code).map((p) => `${s.file}: ${p}`))).toEqual([])
  })

  it('the check reads the calls it is meant to read', () => {
    const count = sources.reduce((n, s) => n + requestCalls(s.code).length, 0)
    expect(count).toBeGreaterThan(20)
  })

  it('rejects a bad path and accepts a good one, whatever the quote style or generics', () => {
    expect(pathProblems("apiClient.get(apiPath('items') + '/' + id)")).toHaveLength(1)
    expect(pathProblems('apiClient.get(`/items/${id}`)')).toHaveLength(1)
    expect(pathProblems("apiClient.get('/items/' + id)")).toHaveLength(1)
    expect(pathProblems('apiClient.get(path)')).toHaveLength(1)
    expect(pathProblems("apiClient.get(apiPath('a'), x)")).toEqual([])
    expect(pathProblems('apiClient.get("/x")')).toEqual([])
    expect(pathProblems('apiClient.get(`/x`)')).toEqual([])
    expect(pathProblems("apiClient.post('/login', { email, n: a + 1 })")).toEqual([])
    expect(pathProblems("apiClient.get<Record<string, (x: number) => void>>(\n  withQuery(apiPath('a', id), params),\n)")).toEqual([])
    expect(pathProblems("apiClient.urlFor('' + base)")).toHaveLength(1)
    expect(pathProblems("apiClient.urlFor(apiPath('files', id, 'content'))")).toEqual([])
  })

  it('fails on a call it cannot read instead of skipping it', () => {
    expect(pathProblems('const f = apiClient.get')).toHaveLength(1)
    expect(pathProblems("apiClient.get<Foo(apiPath('a'))")).toHaveLength(1)
    expect(pathProblems("apiClient.get(apiPath('a')")).toHaveLength(1)
  })
})

describe('the base URL and fetch belong to apiClient.ts', () => {
  // `fetch` as the name of a callback parameter is not the network call.
  const LOCAL_FETCH_CALLS: Record<string, string> = { 'lib/guardedLoad.ts': 'const result = await fetch()' }
  const others = sources.filter((s) => s.file !== 'api/apiClient.ts')

  it('apiBaseUrl and VITE_API_BASE_URL are named nowhere else', () => {
    expect(others.filter((s) => identifierNamed(s.code, 'apiBaseUrl')).map((s) => s.file)).toEqual([])
    expect(others.filter((s) => identifierNamed(s.code, 'VITE_API_BASE_URL')).map((s) => s.file)).toEqual([])
  })

  it('fetch( is called nowhere else', () => {
    const offenders = others.flatMap((s) =>
      fetchCallLines(s.code).filter((line) => LOCAL_FETCH_CALLS[s.file] !== line).map((line) => `${s.file}: ${line}`))
    expect(offenders).toEqual([])
  })

  it('the listed exception is a callback named fetch', () => {
    const source = sources.find((s) => s.file === 'lib/guardedLoad.ts')!
    expect(source.code).toContain('fetch: () => Promise<T>')
    expect(source.code).toContain(LOCAL_FETCH_CALLS['lib/guardedLoad.ts'])
  })

  it('identifierNamed and fetchCallLines flag the bad samples and accept the good ones', () => {
    expect(identifierNamed('const a = import.meta.env.VITE_API_BASE_URL', 'VITE_API_BASE_URL')).toBe(true)
    expect(identifierNamed("const a = 'VITE_API_BASE_URL' // apiBaseUrl", 'VITE_API_BASE_URL')).toBe(false)
    expect(identifierNamed("const a = apiBaseUrl + '/x'", 'apiBaseUrl')).toBe(true)
    expect(fetchCallLines("await fetch('/x')")).toHaveLength(1)
    expect(fetchCallLines("await window.fetch('/x')")).toHaveLength(1)
    expect(fetchCallLines('const r = await fetch()')).toHaveLength(1)
    expect(fetchCallLines("// fetch('/x')\nconst a = 'fetch(1)'")).toEqual([])
    expect(fetchCallLines('const fetcher = { fetch: 1 }')).toEqual([])
  })
})

describe('the scanner reads comments, strings and regular expressions apart', () => {
  it('keeps the code between a string holding /* and a later block comment', () => {
    const code = withoutComments("const type = 'image/*'\nconst keep = 1\n/* gone */ const alsoKept = 2")
    expect(code).toContain('const keep = 1')
    expect(code).toContain('const alsoKept = 2')
    expect(code).not.toContain('gone')
  })

  it('keeps a // inside a string, a template and a regular expression', () => {
    const code = withoutComments("const a = 'http://x' // gone\nconst b = `a${'//'}b`\nconst c = /[/*]\\//g\nconst d = 4")
    expect(code).toContain("'http://x'")
    expect(code).toContain("`a${'//'}b`")
    expect(code).toContain('const d = 4')
    expect(code).not.toContain('gone')
  })

  it('reads only the script blocks of a .vue file', () => {
    const text = '<template><p>apiClient.get(x + 1)</p></template>\n<script setup lang="ts">\nconst a = 1\n</script>'
    expect(scriptText('A.vue', text)).toContain('const a = 1')
    expect(scriptText('A.vue', text)).not.toContain('apiClient')
  })
})
