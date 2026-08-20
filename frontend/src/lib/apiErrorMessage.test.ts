import { describe, expect, it } from 'vitest'
import { ApiError } from '../api/apiClient'
import { tooManyRequestsMessage } from './apiErrorMessage'

describe('tooManyRequestsMessage', () => {
  it('includes the wait when the server told us how long', () => {
    const err = new ApiError(429, 'nope', 'TOO_MANY_REQUESTS', undefined, 42)
    expect(tooManyRequestsMessage(err)).toEqual({
      key: 'errors.tooManyRequestsWithWait',
      params: { seconds: 42 },
    })
  })

  it('falls back to a wait-less sentence when Retry-After was absent', () => {
    const err = new ApiError(429, 'nope', 'TOO_MANY_REQUESTS')
    expect(tooManyRequestsMessage(err)).toEqual({ key: 'errors.tooManyRequests' })
  })
})
