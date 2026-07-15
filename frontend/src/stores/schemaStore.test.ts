import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSchemaStore } from './schemaStore'
import { schemaApi } from '../api/schemaApi'

vi.mock('../api/schemaApi', () => ({ schemaApi: { getAll: vi.fn() } }))

describe('schemaStore', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('loads collections once and caches', async () => {
    vi.mocked(schemaApi.getAll).mockResolvedValue([{ name: 'article', label: 'Article', fields: [], relations: [] }])
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

  it('dedupes concurrent load() calls into a single in-flight fetch (deep-link race: AppShell + view both call load() before either resolves)', async () => {
    let resolveFetch!: (v: unknown) => void
    const pending = new Promise((resolve) => { resolveFetch = resolve })
    vi.mocked(schemaApi.getAll).mockReturnValue(pending as ReturnType<typeof schemaApi.getAll>)
    const store = useSchemaStore()
    const p1 = store.load()
    const p2 = store.load() // fired before p1's fetch has resolved -- must not trigger a second getAll()
    resolveFetch([{ name: 'article', label: 'Article', fields: [], relations: [] }])
    await Promise.all([p1, p2])
    expect(schemaApi.getAll).toHaveBeenCalledOnce()
    expect(store.get('article')?.label).toBe('Article')
  })

  it('allows a retry after a failed load (in-flight guard clears even on error)', async () => {
    vi.mocked(schemaApi.getAll).mockRejectedValueOnce(new Error('boom'))
    const store = useSchemaStore()
    await store.load()
    expect(store.loadError).toBe('boom')
    vi.mocked(schemaApi.getAll).mockResolvedValueOnce([{ name: 'article', label: 'Article', fields: [], relations: [] }])
    await store.load()
    expect(store.loaded).toBe(true)
    expect(schemaApi.getAll).toHaveBeenCalledTimes(2)
  })
})
