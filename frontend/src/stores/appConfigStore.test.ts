import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAppConfigStore } from './appConfigStore'
import * as api from '../api/appConfigApi'
import * as settingsApi from '../api/settingsApi'

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
      oidcEnabled: true, brandName: 'Acme', brandLogoUrl: 'https://x/logo.svg', passwordMinLength: 14,
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

  it('loads passwordMinLength from the config endpoint', async () => {
    vi.spyOn(api, 'getAppConfig').mockResolvedValue({
      oidcEnabled: false, brandName: 'B', brandLogoUrl: null, passwordMinLength: 14,
    })
    const store = useAppConfigStore()

    await store.load()

    expect(store.passwordMinLength).toBe(14)
  })

  it('keeps the safe default when the config endpoint fails', async () => {
    vi.spyOn(api, 'getAppConfig').mockRejectedValue(new Error('down'))
    const store = useAppConfigStore()

    await store.load()

    expect(store.passwordMinLength).toBe(8)
  })

  it('brandInitial is the upper-cased first character', () => {
    const store = useAppConfigStore()
    store.brandName = 'acme'
    expect(store.brandInitial).toBe('A')
    store.brandName = ''
    expect(store.brandInitial).toBe('')
  })

  it('saveBranding updates store state from the response', async () => {
    setActivePinia(createPinia())
    const store = useAppConfigStore()
    vi.spyOn(settingsApi, 'updateBranding').mockResolvedValue({ brandName: 'New', brandLogoUrl: null })
    await store.saveBranding({ brandName: 'New', logoFileId: null })
    expect(store.brandName).toBe('New')
    expect(store.brandLogoUrl).toBeNull()
  })
})
