import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useLanguageStore } from './languageStore'
import { languagesApi } from '../api/languagesApi'

describe('languageStore', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.restoreAllMocks() })

  it('load caches and exposes defaultCode', async () => {
    const spy = vi.spyOn(languagesApi, 'getEnabled').mockResolvedValue([
      { code: 'en', name: 'English', isDefault: true },
      { code: 'zh-TW', name: '繁體中文', isDefault: false },
    ])
    const store = useLanguageStore()
    await store.load()
    await store.load() // cached, no second call
    expect(spy).toHaveBeenCalledTimes(1)
    expect(store.defaultCode).toBe('en')
    expect(store.languages).toHaveLength(2)
  })

  it('records loadError on failure', async () => {
    vi.spyOn(languagesApi, 'getEnabled').mockRejectedValue(new Error('boom'))
    const store = useLanguageStore()
    await store.load()
    expect(store.loadError).toBe('boom')
    expect(store.loaded).toBe(false)
  })

  it('dedupes concurrent load() calls into a single in-flight fetch (FE-24: e.g. dashboard fan-out calling load() before any has resolved)', async () => {
    let resolveFetch!: (v: unknown) => void
    const pending = new Promise((resolve) => { resolveFetch = resolve })
    const spy = vi.spyOn(languagesApi, 'getEnabled')
      .mockReturnValue(pending as ReturnType<typeof languagesApi.getEnabled>)
    const store = useLanguageStore()
    const p1 = store.load()
    const p2 = store.load() // fired before p1's fetch has resolved -- must not trigger a second getEnabled()
    resolveFetch([{ code: 'en', name: 'English', isDefault: true }])
    await Promise.all([p1, p2])
    expect(spy).toHaveBeenCalledOnce()
    expect(store.defaultCode).toBe('en')
  })

  it('allows a retry after a failed load (in-flight guard clears even on error)', async () => {
    const spy = vi.spyOn(languagesApi, 'getEnabled').mockRejectedValueOnce(new Error('boom'))
    const store = useLanguageStore()
    await store.load()
    expect(store.loadError).toBe('boom')
    spy.mockResolvedValueOnce([{ code: 'en', name: 'English', isDefault: true }])
    await store.load()
    expect(store.loaded).toBe(true)
    expect(spy).toHaveBeenCalledTimes(2)
  })
})
