import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSchemaStore } from './schemaStore'
import { schemaApi } from '../api/schemaApi'

vi.mock('../api/schemaApi', () => ({ schemaApi: { getAll: vi.fn() } }))

describe('schemaStore', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('loads collections once and caches', async () => {
    vi.mocked(schemaApi.getAll).mockResolvedValue([{ name: 'article', label: 'Article', fields: [] }])
    const store = useSchemaStore()
    await store.load()
    await store.load() // second call should not re-fetch
    expect(schemaApi.getAll).toHaveBeenCalledOnce()
    expect(store.get('article')?.label).toBe('Article')
  })

  it('records loadError on failure without throwing', async () => {
    vi.mocked(schemaApi.getAll).mockRejectedValue(new Error('boom'))
    const store = useSchemaStore()
    await store.load()
    expect(store.loadError).toBe('boom')
    expect(store.collections).toEqual([])
  })
})
