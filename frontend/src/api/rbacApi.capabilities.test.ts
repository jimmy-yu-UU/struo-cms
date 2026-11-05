import { describe, it, expect, vi, beforeEach } from 'vitest'
import { apiClient } from './apiClient'
import { rbacApi } from './rbacApi'

vi.mock('./apiClient', () => ({
  apiClient: { get: vi.fn(), put: vi.fn() },
}))

describe('rbacApi capabilities', () => {
  beforeEach(() => vi.clearAllMocks())

  it('listCapabilities hits /capabilities', async () => {
    vi.mocked(apiClient.get).mockResolvedValue([])
    await rbacApi.listCapabilities()
    expect(apiClient.get).toHaveBeenCalledWith('/capabilities')
  })

  it('getRoleCapabilities hits /roles/{id}/capabilities', async () => {
    vi.mocked(apiClient.get).mockResolvedValue(['login.unlock'])
    expect(await rbacApi.getRoleCapabilities('r1')).toEqual(['login.unlock'])
    expect(apiClient.get).toHaveBeenCalledWith('/roles/r1/capabilities')
  })

  it('putRoleCapabilities PUTs the code array', async () => {
    vi.mocked(apiClient.put).mockResolvedValue(['login.unlock'])
    await rbacApi.putRoleCapabilities('r1', ['login.unlock'])
    expect(apiClient.put).toHaveBeenCalledWith('/roles/r1/capabilities', ['login.unlock'])
  })
})
