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

  it('dedupes concurrent load() calls into a single in-flight fetch (e.g. CollectionListView switching collections mid-load, its onMounted and watch(name) handler both calling load() before either has resolved)', async () => {
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

  it('reload() bypasses the loaded guard and refetches', async () => {
    const spy = vi.spyOn(languagesApi, 'getEnabled').mockResolvedValueOnce([
      { code: 'en', name: 'English', isDefault: true },
    ])
    const store = useLanguageStore()
    await store.load()
    expect(store.languages).toHaveLength(1)

    spy.mockResolvedValueOnce([
      { code: 'en', name: 'English', isDefault: true },
      { code: 'zh-CN', name: '简体中文', isDefault: false },
    ])
    await store.reload()
    expect(store.languages).toHaveLength(2)
    expect(spy).toHaveBeenCalledTimes(2)
  })

  it('reload() awaits an in-flight load() before resetting (no race with a concurrent load())', async () => {
    let resolveFetch!: (v: unknown) => void
    const pending = new Promise((resolve) => { resolveFetch = resolve })
    const spy = vi.spyOn(languagesApi, 'getEnabled')
      .mockReturnValueOnce(pending as ReturnType<typeof languagesApi.getEnabled>)
      .mockResolvedValueOnce([{ code: 'en', name: 'English', isDefault: true }, { code: 'fr', name: 'Français', isDefault: false }])
    const store = useLanguageStore()
    const loadP = store.load() // in-flight, not yet resolved
    const reloadP = store.reload() // must wait for loadP's fetch to settle before resetting `loaded`
    resolveFetch([{ code: 'en', name: 'English', isDefault: true }])
    await Promise.all([loadP, reloadP])
    expect(spy).toHaveBeenCalledTimes(2) // one for the in-flight load, one for reload's own refetch
    expect(store.languages).toHaveLength(2)
  })
})
