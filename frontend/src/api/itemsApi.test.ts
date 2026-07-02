import { describe, it, expect, vi, beforeEach } from 'vitest'
import { itemsApi } from './itemsApi'
import { apiClient } from './apiClient'

vi.mock('./apiClient', () => ({
  apiClient: { getRaw: vi.fn(), get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
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

describe('itemsApi mutations', () => {
  beforeEach(() => vi.clearAllMocks())

  it('get fetches a single item by id', async () => {
    const spy = vi.spyOn(apiClient, 'get').mockResolvedValue({ id: '1', status: 'draft' })
    const res = await itemsApi.get('article', '1')
    expect(spy).toHaveBeenCalledWith('/items/article/1')
    expect(res).toEqual({ id: '1', status: 'draft' })
  })

  it('get passes locale query when provided', async () => {
    const spy = vi.spyOn(apiClient, 'get').mockResolvedValue({})
    await itemsApi.get('article', '1', { locale: 'zh-TW' })
    expect(spy).toHaveBeenCalledWith('/items/article/1?locale=zh-TW')
  })

  it('create posts the payload', async () => {
    const spy = vi.spyOn(apiClient, 'post').mockResolvedValue({ id: '9' })
    await itemsApi.create('article', { status: 'draft' })
    expect(spy).toHaveBeenCalledWith('/items/article', { status: 'draft' })
  })

  it('update puts the payload', async () => {
    const spy = vi.spyOn(apiClient, 'put').mockResolvedValue({ id: '1' })
    await itemsApi.update('article', '1', { status: 'published' })
    expect(spy).toHaveBeenCalledWith('/items/article/1', { status: 'published' })
  })

  it('remove deletes by id', async () => {
    const spy = vi.spyOn(apiClient, 'delete').mockResolvedValue(undefined)
    await itemsApi.remove('article', '1')
    expect(spy).toHaveBeenCalledWith('/items/article/1')
  })
})
