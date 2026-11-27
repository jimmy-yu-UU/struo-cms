import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSchemaStore } from './schemaStore'
import { schemaApi } from '../api/schemaApi'
import { useUiLocaleStore } from './uiLocaleStore'

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

  it('a load that fails after reset() leaves loadError empty and the store retryable', async () => {
    let rejectFetch!: (e: Error) => void
    const pending = new Promise<never>((_, reject) => { rejectFetch = reject })
    vi.mocked(schemaApi.getAll).mockReturnValue(pending as never)
    const store = useSchemaStore()
    const p = store.load()
    store.reset()
    rejectFetch(new Error('late failure'))
    await p
    expect(store.loadError).toBe('')
    expect(store.loadPromise).toBeNull()
  })
})

describe('schemaStore localisation', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })
  afterEach(() => { useUiLocaleStore().apply('en') })

  const server = [{ name: 'role', label: 'Role', group: 'System', fields: [], relations: [] }]

  it('exposes core labels in the UI locale and follows a locale switch', async () => {
    vi.mocked(schemaApi.getAll).mockResolvedValue(server)
    const store = useSchemaStore()
    await store.load()
    expect(store.get('role')?.label).toBe('Role')
    useUiLocaleStore().apply('zh-TW')
    expect(store.get('role')?.label).toBe('角色')
    expect(store.collections[0].group).toBe('系統')
    useUiLocaleStore().apply('en')
    expect(store.collections[0].label).toBe('Role')
  })

  it('keeps the raw server data', async () => {
    vi.mocked(schemaApi.getAll).mockResolvedValue(server)
    const store = useSchemaStore()
    await store.load()
    useUiLocaleStore().apply('zh-TW')
    expect(store.raw).toEqual(server)
  })

  it('shows a fork collection as the server sends it', async () => {
    vi.mocked(schemaApi.getAll).mockResolvedValue([{ name: 'article', label: 'Role', fields: [], relations: [] }])
    const store = useSchemaStore()
    await store.load()
    useUiLocaleStore().apply('zh-TW')
    expect(store.get('article')?.label).toBe('Role')
  })

  it('accepts an assigned collections list and clears it on reset', () => {
    const store = useSchemaStore()
    store.collections = [{ name: 'role', label: 'Role', fields: [], relations: [] }]
    expect(store.get('role')).toBeDefined()
    store.reset()
    expect(store.collections).toEqual([])
    expect(store.raw).toEqual([])
  })
})
