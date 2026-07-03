import { describe, it, expect } from 'vitest'
import { buildListQuery } from './buildListQuery'

describe('buildListQuery', () => {
  it('maps page/rows to limit/offset', () => {
    expect(buildListQuery(0, 25)).toEqual({ limit: '25', offset: '0' })
    expect(buildListQuery(2, 10)).toEqual({ limit: '10', offset: '20' })
  })

  it('includes sort when provided (asc and desc tokens pass through)', () => {
    expect(buildListQuery(0, 25, 'title')).toEqual({ limit: '25', offset: '0', sort: 'title' })
    expect(buildListQuery(0, 25, '-createdAt')).toEqual({ limit: '25', offset: '0', sort: '-createdAt' })
  })

  it('includes search only when non-empty', () => {
    expect(buildListQuery(0, 25, undefined, 'hello')).toEqual({ limit: '25', offset: '0', search: 'hello' })
    expect(buildListQuery(0, 25, undefined, '')).toEqual({ limit: '25', offset: '0' })
    expect(buildListQuery(0, 25, undefined, '   ')).toEqual({ limit: '25', offset: '0' })
  })
})

describe('buildListQuery filter + locale', () => {
  it('emits filter[field][op]=value', () => {
    const p = buildListQuery(0, 25, undefined, undefined, { categoryId: { op: '_eq', value: 'abc' } })
    expect(p['filter[categoryId][_eq]']).toBe('abc')
  })
  it('emits locale when provided', () => {
    const p = buildListQuery(0, 25, undefined, undefined, undefined, 'zh-TW')
    expect(p.locale).toBe('zh-TW')
  })
})
