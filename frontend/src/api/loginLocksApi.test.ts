import { describe, it, expect, vi, beforeEach } from 'vitest'
import { apiClient } from './apiClient'
import { loginLocksApi } from './loginLocksApi'

vi.mock('./apiClient', () => ({
  apiClient: { post: vi.fn(), delete: vi.fn() },
}))

describe('loginLocksApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('query POSTs the emails and returns the entries as answered', async () => {
    const answer = [{ email: 'a@x.test', locked: true, retryAfterSeconds: 90, clearable: false }]
    vi.mocked(apiClient.post).mockResolvedValue(answer)
    await expect(loginLocksApi.query(['a@x.test'])).resolves.toEqual(answer)
    expect(apiClient.post).toHaveBeenCalledWith('/login-locks/query', { emails: ['a@x.test'] })
  })

  it('clear DELETEs the lock with the email URL-encoded', async () => {
    vi.mocked(apiClient.delete).mockResolvedValue(undefined)
    await loginLocksApi.clear('a+b@x.test')
    expect(apiClient.delete).toHaveBeenCalledWith('/login-locks?email=a%2Bb%40x.test')
  })
})
