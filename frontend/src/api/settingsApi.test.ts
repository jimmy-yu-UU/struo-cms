import { describe, it, expect, vi, beforeEach } from 'vitest'
import { apiClient } from './apiClient'
import { updateBranding } from './settingsApi'

describe('settingsApi', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('PUTs branding and returns the effective config', async () => {
    const put = vi.spyOn(apiClient, 'put').mockResolvedValue({ brandName: 'B', brandLogoUrl: '/api/files/x/content' })
    const res = await updateBranding({ brandName: 'B', logoFileId: 'x' })
    expect(put).toHaveBeenCalledWith('/settings/branding', { brandName: 'B', logoFileId: 'x' })
    expect(res.brandLogoUrl).toBe('/api/files/x/content')
  })
})
