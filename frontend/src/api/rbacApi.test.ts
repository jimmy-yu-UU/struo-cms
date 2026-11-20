import { describe, it, expect, vi, beforeEach } from 'vitest'
import { apiClient } from './apiClient'
import { rbacApi } from './rbacApi'

vi.mock('./apiClient', () => ({
  apiClient: { get: vi.fn(), put: vi.fn(), putEnveloped: vi.fn() },
}))

describe('rbacApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('getRolePermissions hits /roles/{id}/permissions', async () => {
    vi.mocked(apiClient.get).mockResolvedValue([])
    await rbacApi.getRolePermissions('r1')
    expect(apiClient.get).toHaveBeenCalledWith('/roles/r1/permissions')
  })

  it('putRolePermissions PUTs the entries array and reports the role version of meta.version', async () => {
    vi.mocked(apiClient.putEnveloped).mockResolvedValue({ data: [], meta: { version: 5 } })
    const entries = [{ collection: 'article', canRead: true, canWrite: false, canDelete: false }]
    const saved = await rbacApi.putRolePermissions('r1', entries)
    expect(apiClient.putEnveloped).toHaveBeenCalledWith('/roles/r1/permissions', entries)
    expect(saved).toEqual({ entries: [], version: 5 })
  })

  it('putRolePermissions sends the expected version as ?version=', async () => {
    vi.mocked(apiClient.putEnveloped).mockResolvedValue({ data: [], meta: { version: 4 } })
    await rbacApi.putRolePermissions('r1', [], 3)
    expect(apiClient.putEnveloped).toHaveBeenCalledWith('/roles/r1/permissions?version=3', [])
  })

  it('putRolePermissions reports no version when the response carries none', async () => {
    vi.mocked(apiClient.putEnveloped).mockResolvedValue({ data: [], meta: undefined })
    expect((await rbacApi.putRolePermissions('r1', [])).version).toBeUndefined()
  })

  it('putRolePermissions answers the entries it sent, and no version, when the response has no data', async () => {
    vi.mocked(apiClient.putEnveloped).mockResolvedValue({ data: undefined, meta: undefined })
    const entries = [{ collection: 'article', canRead: true, canWrite: false, canDelete: false }]
    expect(await rbacApi.putRolePermissions('r1', entries, 3)).toEqual({ entries, version: undefined })
  })

  it('getEffectivePermissions hits /users/{id}/effective-permissions when roleIds is omitted', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ isSuperAdmin: false, permissions: {} })
    await rbacApi.getEffectivePermissions('u1')
    expect(apiClient.get).toHaveBeenCalledWith('/users/u1/effective-permissions')
  })

  it('getEffectivePermissions appends ?roles=<csv> when roleIds is provided', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ isSuperAdmin: false, permissions: {} })
    await rbacApi.getEffectivePermissions('u1', ['a', 'b'])
    expect(apiClient.get).toHaveBeenCalledWith('/users/u1/effective-permissions?roles=a%2Cb')
  })

  it('getEffectivePermissions appends ?roles= (empty) for an empty roleIds array', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ isSuperAdmin: false, permissions: {} })
    await rbacApi.getEffectivePermissions('u1', [])
    expect(apiClient.get).toHaveBeenCalledWith('/users/u1/effective-permissions?roles=')
  })
})
