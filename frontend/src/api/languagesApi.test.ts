import { describe, it, expect, vi, beforeEach } from 'vitest'
import { languagesApi } from './languagesApi'
import { apiClient } from './apiClient'

describe('languagesApi', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('getEnabled fetches /languages and returns the list', async () => {
    const spy = vi.spyOn(apiClient, 'get').mockResolvedValue([
      { code: 'en', name: 'English', isDefault: true },
    ])
    const res = await languagesApi.getEnabled()
    expect(spy).toHaveBeenCalledWith('/languages')
    expect(res).toEqual([{ code: 'en', name: 'English', isDefault: true }])
  })
})
