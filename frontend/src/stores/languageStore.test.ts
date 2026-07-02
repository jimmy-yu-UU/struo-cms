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
})
