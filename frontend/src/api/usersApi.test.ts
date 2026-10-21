import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiClient } from './apiClient'
import { usersApi } from './usersApi'

vi.mock('./apiClient', () => ({ apiClient: { put: vi.fn(), post: vi.fn() } }))

describe('usersApi', () => {
  beforeEach(() => { vi.mocked(apiClient.put).mockReset().mockResolvedValue(undefined) })

  it('sends the self-service shape with currentPassword', async () => {
    await usersApi.changePassword('u1', { newPassword: 'n', currentPassword: 'c' })
    expect(apiClient.put).toHaveBeenCalledExactlyOnceWith('/users/u1/password', {
      newPassword: 'n', currentPassword: 'c',
    })
  })

  it('omits currentPassword on the admin-reset shape', async () => {
    await usersApi.changePassword('u2', { newPassword: 'n' })
    expect(apiClient.put).toHaveBeenCalledExactlyOnceWith('/users/u2/password', { newPassword: 'n' })
    // toHaveBeenCalledWith uses non-strict equality, which treats an explicit
    // `currentPassword: undefined` key as equal to the key being absent — but the endpoint's
    // self-vs-admin logic keys on presence, not value, so presence needs its own assertion.
    const [, body] = vi.mocked(apiClient.put).mock.calls[0]!
    expect('currentPassword' in (body as object)).toBe(false)
  })

  it('posts the create-user body to /users and returns the created user', async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ id: 'n1', email: 'a@b.c', name: 'A' })
    const created = await usersApi.create({ email: 'a@b.c', password: 'pw', name: 'A' })
    expect(apiClient.post).toHaveBeenCalledExactlyOnceWith('/users', { email: 'a@b.c', password: 'pw', name: 'A' })
    expect(created.id).toBe('n1')
  })
})
