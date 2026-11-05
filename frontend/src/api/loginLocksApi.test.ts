import { describe, it, expect, vi, beforeEach } from 'vitest'
import { apiClient } from './apiClient'
import { loginLocksApi } from './loginLocksApi'

vi.mock('./apiClient', () => ({
  apiClient: { get: vi.fn(), delete: vi.fn() },
}))

describe('loginLocksApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('get reads the lock state with the email URL-encoded', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ locked: false, retryAfterSeconds: 0 })
    await loginLocksApi.get('a+b@x.test')
    expect(apiClient.get).toHaveBeenCalledWith('/login-locks?email=a%2Bb%40x.test')
  })

  it('clear DELETEs the lock with the email URL-encoded', async () => {
    vi.mocked(apiClient.delete).mockResolvedValue(undefined)
    await loginLocksApi.clear('a+b@x.test')
    expect(apiClient.delete).toHaveBeenCalledWith('/login-locks?email=a%2Bb%40x.test')
  })
})
