import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiClient } from './apiClient'
import { usersApi } from './usersApi'

vi.mock('./apiClient', () => ({ apiClient: { put: vi.fn() } }))

describe('usersApi', () => {
  beforeEach(() => { vi.mocked(apiClient.put).mockReset().mockResolvedValue(undefined) })

  it('sends the self-service shape with currentPassword', async () => {
    await usersApi.changePassword('u1', { newPassword: 'n', currentPassword: 'c' })
    expect(apiClient.put).toHaveBeenCalledWith('/users/u1/password', {
      newPassword: 'n', currentPassword: 'c',
    })
  })

  it('omits currentPassword on the admin-reset shape', async () => {
    await usersApi.changePassword('u2', { newPassword: 'n' })
    expect(apiClient.put).toHaveBeenCalledWith('/users/u2/password', { newPassword: 'n' })
  })
})
