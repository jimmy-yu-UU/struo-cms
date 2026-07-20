import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAppConfigStore } from './appConfigStore'
import * as api from '../api/appConfigApi'

describe('appConfigStore', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.restoreAllMocks())

  it('has safe defaults', () => {
    const store = useAppConfigStore()
    expect(store.oidcEnabled).toBe(false)
    expect(store.brandName).toBe('StruoCMS')
    expect(store.brandLogoUrl).toBeNull()
  })

  it('load() populates state from the API', async () => {
    vi.spyOn(api, 'getAppConfig').mockResolvedValue({
      oidcEnabled: true, brandName: 'Acme', brandLogoUrl: 'https://x/logo.svg',
    })
    const store = useAppConfigStore()
    await store.load()
    expect(store.oidcEnabled).toBe(true)
    expect(store.brandName).toBe('Acme')
    expect(store.brandLogoUrl).toBe('https://x/logo.svg')
  })

  it('load() keeps defaults when the API fails', async () => {
    vi.spyOn(api, 'getAppConfig').mockRejectedValue(new Error('network'))
    const store = useAppConfigStore()
    await store.load()
    expect(store.brandName).toBe('StruoCMS')
    expect(store.oidcEnabled).toBe(false)
  })

  it('brandInitial is the upper-cased first character', () => {
    const store = useAppConfigStore()
    store.brandName = 'acme'
    expect(store.brandInitial).toBe('A')
    store.brandName = ''
    expect(store.brandInitial).toBe('')
  })
})
