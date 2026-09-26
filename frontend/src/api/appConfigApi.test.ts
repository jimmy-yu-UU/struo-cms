import { describe, it, expect, vi, afterEach } from 'vitest'
import { getAppConfig } from './appConfigApi'
import { apiClient } from './apiClient'

describe('appConfigApi', () => {
  afterEach(() => vi.restoreAllMocks())

  it('fetches /config and returns the parsed config', async () => {
    const payload = {
      oidcEnabled: true,
      brandName: 'Acme',
      brandLogoUrl: 'https://x/logo.svg',
      passwordMinLength: 8,
      uiLocales: ['zh-TW', 'en'],
      uiDefaultLocale: 'zh-TW',
    }
    const spy = vi.spyOn(apiClient, 'get').mockResolvedValue(payload)
    const result = await getAppConfig()
    expect(spy).toHaveBeenCalledWith('/config')
    expect(result).toEqual(payload)
  })
})
