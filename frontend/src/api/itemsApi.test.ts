import { describe, it, expect, vi, beforeEach } from 'vitest'
import { itemsApi } from './itemsApi'
import { apiClient } from './apiClient'

vi.mock('./apiClient', () => ({
  apiClient: { getRaw: vi.fn() },
}))

describe('itemsApi.list', () => {
  beforeEach(() => vi.clearAllMocks())

  it('builds the query string and unwraps { data, meta.total }', async () => {
    vi.mocked(apiClient.getRaw).mockResolvedValue({ data: [{ id: '1' }], meta: { total: 7 } })
    const res = await itemsApi.list('article', { page: 1, rows: 10, sort: '-createdAt', search: 'x' })
    expect(apiClient.getRaw).toHaveBeenCalledWith('/items/article?limit=10&offset=10&sort=-createdAt&search=x')
    expect(res).toEqual({ data: [{ id: '1' }], total: 7 })
  })

  it('omits sort/search when not provided', async () => {
    vi.mocked(apiClient.getRaw).mockResolvedValue({ data: [], meta: { total: 0 } })
    await itemsApi.list('article', { page: 0, rows: 25 })
    expect(apiClient.getRaw).toHaveBeenCalledWith('/items/article?limit=25&offset=0')
  })
})
