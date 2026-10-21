import { describe, it, expect } from 'vitest'
import { safeRedirect, encodeRedirectParam } from './safeRedirect'

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

describe('encodeRedirectParam', () => {
  it('keeps slashes readable and encodes the query delimiters', () => {
    expect(encodeRedirectParam('/')).toBe('/')
    expect(encodeRedirectParam('/c/a?x=1&y=2')).toBe('/c/a%3Fx%3D1%26y%3D2')
  })
})
