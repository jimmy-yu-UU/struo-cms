import { describe, it, expect } from 'vitest'
import { mediaTypeFilter, mediaSort } from './mediaQuery'

describe('mediaTypeFilter', () => {
  it('returns undefined for all', () => {
    expect(mediaTypeFilter('all')).toBeUndefined()
  })
  it('maps image to a contentType starts-with image/', () => {
    expect(mediaTypeFilter('image')).toEqual({ contentType: { op: '_starts_with', value: 'image/' } })
  })
  it('maps video to a contentType starts-with video/', () => {
    expect(mediaTypeFilter('video')).toEqual({ contentType: { op: '_starts_with', value: 'video/' } })
  })
})

describe('mediaSort', () => {
  it('maps newest to -createdAt', () => {
    expect(mediaSort('newest')).toBe('-createdAt')
  })
  it('maps name to fileName', () => {
    expect(mediaSort('name')).toBe('fileName')
  })
})
