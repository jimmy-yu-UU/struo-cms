import { describe, it, expect } from 'vitest'
import { apiBaseUrl } from './apiClient'

describe('apiBaseUrl', () => {
  it('defaults to /api when no env override is set', () => {
    expect(apiBaseUrl).toBe('/api')
  })
})
