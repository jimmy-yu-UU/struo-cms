import { describe, it, expect } from 'vitest'
import { safeRedirect, encodeRedirectParam, loginRedirectQuery, ssoReturnUrl } from './safeRedirect'

const BACKSLASH = String.fromCharCode(92)

describe('safeRedirect', () => {
  it.each([
    ['/collections/article/5', '/collections/article/5'],
    ['/collections/article?page=2&q=a#top', '/collections/article?page=2&q=a#top'],
    ['/', '/'],
  ])('accepts the same-origin path %s', (input, expected) => {
    expect(safeRedirect(input)).toBe(expected)
  })

  it.each([
    ['//evil.example'],
    ['/' + BACKSLASH + 'evil.example'],
    ['https://evil.example'],
    ['/\t/evil.example'],
    ['/\n/evil.example'],
    ['/\r/evil.example'],
    ['/ok\u007f'],
    ['javascript:alert(1)'],
    ['collections/article'],
    [''],
  ])('rejects %j', (input) => {
    expect(safeRedirect(input)).toBeNull()
  })

  it.each([[undefined], [null], [42], [['/a']], [{}]])('rejects the non-string %j', (input) => {
    expect(safeRedirect(input)).toBeNull()
  })
})

describe('loginRedirectQuery', () => {
  it('carries a normal path', () => {
    expect(loginRedirectQuery('/media?x=1')).toEqual({ redirect: '/media?x=1' })
  })

  it.each([['/'], ['/login'], ['/login?reason=expired'], ['//evil'], [undefined]])(
    'carries nothing for %j',
    (p) => { expect(loginRedirectQuery(p)).toBeUndefined() },
  )
})

describe('encodeRedirectParam', () => {
  it('keeps slashes readable and encodes the query delimiters', () => {
    expect(encodeRedirectParam('/')).toBe('/')
    expect(encodeRedirectParam('/c/a?x=1&y=2')).toBe('/c/a%3Fx%3D1%26y%3D2')
  })
})

describe('ssoReturnUrl', () => {
  const ORIGIN = 'https://admin.example.com'
  it.each([
    ['/api', '/x?a=1', '/x?a=1'],
    ['https://api.example.com/api', '/x?a=1', ORIGIN + '/x?a=1'],
    ['HTTPS://API.EXAMPLE.COM/api', '/x', ORIGIN + '/x'],
    ['//api.example.com/api', '/x', ORIGIN + '/x'],
    [ORIGIN + '/api', '/x', '/x'],
  ])('apiBase %s with path %s gives %s', (apiBase, path, expected) => {
    expect(ssoReturnUrl(apiBase, path, ORIGIN)).toBe(expected)
  })
})
