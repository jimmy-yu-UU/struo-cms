import { describe, expect, it } from 'vitest'
import { apiPath, withQuery } from './apiPath'
import { ApiError } from './apiClient'

describe('apiPath', () => {
  it('joins plain segments under a leading slash', () => {
    expect(apiPath('items', 'article', '3f2c')).toBe('/items/article/3f2c')
  })

  it.each([
    ['?', '%3F'],
    ['#', '%23'],
    ['/', '%2F'],
    ['%', '%25'],
    [' ', '%20'],
    ['中文', '%E4%B8%AD%E6%96%87'],
  ])('encodes %j inside a segment as %s', (raw, encoded) => {
    expect(apiPath('items', 'article', `a${raw}b`)).toBe(`/items/article/a${encoded}b`)
  })

  it('keeps a query-looking id a single segment', () => {
    expect(apiPath('items', 'article', 'abc?purge=true')).toBe('/items/article/abc%3Fpurge%3Dtrue')
  })

  it('keeps a traversal-looking id a single segment', () => {
    const id = 'x/../../user/22222222-2222-2222-2222-222222222222'
    const path = apiPath('items', 'article', id)
    expect(path).toBe('/items/article/x%2F..%2F..%2Fuser%2F22222222-2222-2222-2222-222222222222')
    expect(path.split('/')).toHaveLength(4)
  })

  it('accepts numeric segments', () => {
    expect(apiPath('items', 'article', 'abc', 'revisions', 7)).toBe('/items/article/abc/revisions/7')
  })

  it('leaves GUIDs, collection names and numbers byte-identical', () => {
    const guid = '0b6f6a2e-7c1d-4f7e-9a53-0c5d2b1e9f10'
    expect(apiPath('items', 'mediafolder', guid)).toBe(`/items/mediafolder/${guid}`)
  })

  it.each([[''], ['.'], ['..']])('rejects the segment %j with a 404 NOT_FOUND ApiError', (segment) => {
    let thrown: unknown
    try { apiPath('items', 'article', segment) } catch (e) { thrown = e }
    expect(thrown).toBeInstanceOf(ApiError)
    expect((thrown as ApiError).status).toBe(404)
    expect((thrown as ApiError).code).toBe('NOT_FOUND')
  })

  it('accepts a segment that merely contains dots', () => {
    expect(apiPath('items', 'article', '...')).toBe('/items/article/...')
    expect(apiPath('items', 'article', 'a.b')).toBe('/items/article/a.b')
  })
})

describe('withQuery', () => {
  it('returns the path unchanged without parameters', () => {
    expect(withQuery('/items/a')).toBe('/items/a')
    expect(withQuery('/items/a', new URLSearchParams())).toBe('/items/a')
  })

  it('appends parameters through URLSearchParams', () => {
    expect(withQuery('/items/a', new URLSearchParams({ purge: 'true' }))).toBe('/items/a?purge=true')
  })

  it('encodes parameter values', () => {
    const params = new URLSearchParams()
    params.set('email', 'a+b@c.d')
    expect(withQuery('/login-locks', params)).toBe('/login-locks?email=a%2Bb%40c.d')
  })
})
