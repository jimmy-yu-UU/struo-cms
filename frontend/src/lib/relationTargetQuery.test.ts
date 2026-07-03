import { describe, it, expect } from 'vitest'
import { relationTargetQuery } from './relationTargetQuery'

describe('relationTargetQuery', () => {
  it('builds paginated search params with locale', () => {
    const p = relationTargetQuery({ page: 1, rows: 20, search: 'foo', locale: 'en' })
    expect(p.limit).toBe('20')
    expect(p.offset).toBe('20')
    expect(p.search).toBe('foo')
    expect(p.locale).toBe('en')
  })
  it('passes a filter through (RelatedList inbound)', () => {
    const p = relationTargetQuery({ page: 0, rows: 10, filter: { categoryId: { op: '_eq', value: 'x' } } })
    expect(p['filter[categoryId][_eq]']).toBe('x')
  })
})
