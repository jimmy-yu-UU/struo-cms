import { describe, it, expect, vi, beforeEach } from 'vitest'
import { apiClient } from './apiClient'
import { rbacApi } from './rbacApi'

vi.mock('./apiClient', () => ({
  apiClient: { get: vi.fn(), put: vi.fn() },
}))

describe('rbacApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('getRolePermissions hits /roles/{id}/permissions', async () => {
    vi.mocked(apiClient.get).mockResolvedValue([])
    await rbacApi.getRolePermissions('r1')
    expect(apiClient.get).toHaveBeenCalledWith('/roles/r1/permissions')
  })

  it('putRolePermissions PUTs the entries array', async () => {
    vi.mocked(apiClient.put).mockResolvedValue([])
    const entries = [{ collection: 'article', canRead: true, canWrite: false, canDelete: false }]
    await rbacApi.putRolePermissions('r1', entries)
    expect(apiClient.put).toHaveBeenCalledWith('/roles/r1/permissions', entries)
  })

  it('getEffectivePermissions hits /users/{id}/effective-permissions', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ isSuperAdmin: false, permissions: {} })
    await rbacApi.getEffectivePermissions('u1')
    expect(apiClient.get).toHaveBeenCalledWith('/users/u1/effective-permissions')
  })
})
