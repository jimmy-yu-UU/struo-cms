import { describe, it, expect } from 'vitest'
import { buildListQuery } from './buildListQuery'

describe('buildListQuery', () => {
  it('maps page/rows to limit/offset', () => {
    expect(buildListQuery(0, 25)).toEqual({ limit: '25', offset: '0' })
    expect(buildListQuery(2, 10)).toEqual({ limit: '10', offset: '20' })
  })

  it('includes sort when provided (asc and desc tokens pass through)', () => {
    expect(buildListQuery(0, 25, { sort: 'title' })).toEqual({ limit: '25', offset: '0', sort: 'title' })
    expect(buildListQuery(0, 25, { sort: '-createdAt' })).toEqual({ limit: '25', offset: '0', sort: '-createdAt' })
  })

  it('includes search only when non-empty', () => {
    expect(buildListQuery(0, 25, { search: 'hello' })).toEqual({ limit: '25', offset: '0', search: 'hello' })
    expect(buildListQuery(0, 25, { search: '' })).toEqual({ limit: '25', offset: '0' })
    expect(buildListQuery(0, 25, { search: '   ' })).toEqual({ limit: '25', offset: '0' })
  })
})

describe('buildListQuery filter + locale', () => {
  it('emits filter[field][op]=value', () => {
    const p = buildListQuery(0, 25, { filter: { categoryId: { op: '_eq', value: 'abc' } } })
    expect(p['filter[categoryId][_eq]']).toBe('abc')
  })
  it('emits locale when provided', () => {
    const p = buildListQuery(0, 25, { locale: 'zh-TW' })
    expect(p.locale).toBe('zh-TW')
  })
})

describe('buildListQuery deleted mode', () => {
  it('omits deleted for undefined or exclude (server default)', () => {
    expect(buildListQuery(0, 25).deleted).toBeUndefined()
    expect(buildListQuery(0, 25, { deleted: 'exclude' }).deleted).toBeUndefined()
  })
  it('emits deleted for only and with', () => {
    expect(buildListQuery(0, 25, { deleted: 'only' }).deleted).toBe('only')
    expect(buildListQuery(0, 25, { deleted: 'with' }).deleted).toBe('with')
  })
})

describe('buildListQuery deep', () => {
  it('omits deep when not provided or empty (no new key emitted, byte-identical to before)', () => {
    expect(buildListQuery(0, 25)).toEqual({ limit: '25', offset: '0' })
    expect(buildListQuery(0, 25, { deep: [] })).toEqual({ limit: '25', offset: '0' })
  })
  it('joins multiple deep entries with a comma, mirroring itemsApi.get', () => {
    const p = buildListQuery(0, 25, { deep: ['parent'] })
    expect(p.deep).toBe('parent')
    const p2 = buildListQuery(0, 25, { deep: ['category', 'tags'] })
    expect(p2.deep).toBe('category,tags')
  })
})
