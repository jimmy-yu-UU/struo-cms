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

describe('buildListQuery deleted mode', () => {
  it('omits deleted for undefined or exclude (server default)', () => {
    expect(buildListQuery(0, 25).deleted).toBeUndefined()
    expect(buildListQuery(0, 25, undefined, undefined, undefined, undefined, 'exclude').deleted).toBeUndefined()
  })
  it('emits deleted for only and with', () => {
    expect(buildListQuery(0, 25, undefined, undefined, undefined, undefined, 'only').deleted).toBe('only')
    expect(buildListQuery(0, 25, undefined, undefined, undefined, undefined, 'with').deleted).toBe('with')
  })
})

describe('buildListQuery deep', () => {
  it('omits deep when not provided or empty (no new key emitted, byte-identical to before)', () => {
    expect(buildListQuery(0, 25)).toEqual({ limit: '25', offset: '0' })
    expect(buildListQuery(0, 25, undefined, undefined, undefined, undefined, undefined, [])).toEqual({ limit: '25', offset: '0' })
  })
  it('joins multiple deep entries with a comma, mirroring itemsApi.get', () => {
    const p = buildListQuery(0, 25, undefined, undefined, undefined, undefined, undefined, ['parent'])
    expect(p.deep).toBe('parent')
    const p2 = buildListQuery(0, 25, undefined, undefined, undefined, undefined, undefined, ['category', 'tags'])
    expect(p2.deep).toBe('category,tags')
  })
})
