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

  it('list forwards filter and locale to the query string', async () => {
    ;(apiClient.getRaw as any).mockResolvedValue({ data: [], meta: { total: 0 } })
    await itemsApi.list('article', { page: 0, rows: 10, filter: { categoryId: { op: '_eq', value: 'x' } }, locale: 'en' })
    const path = (apiClient.getRaw as any).mock.calls[0][0] as string
    expect(path).toContain('filter%5BcategoryId%5D%5B_eq%5D=x')
    expect(path).toContain('locale=en')
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

  it('get appends deep and locale', async () => {
    ;(apiClient.get as any).mockResolvedValue({})
    await itemsApi.get('article', '1', { locale: 'zh-TW', deep: ['category', 'tags'] })
    expect(apiClient.get).toHaveBeenCalledWith('/items/article/1?locale=zh-TW&deep=category%2Ctags')
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
