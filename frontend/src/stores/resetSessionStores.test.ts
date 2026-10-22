import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { resetSessionStores } from './resetSessionStores'
import { useSchemaStore } from './schemaStore'
import { useLanguageStore } from './languageStore'
import { useConfirmStore } from './confirmStore'
import { useThemeStore } from './themeStore'
import { useUiLocaleStore } from './uiLocaleStore'
import { useAppConfigStore } from './appConfigStore'
import { schemaApi } from '../api/schemaApi'

vi.mock('../api/schemaApi', () => ({ schemaApi: { getAll: vi.fn() } }))
vi.mock('../api/languagesApi', () => ({ languagesApi: { getEnabled: vi.fn() } }))

describe('resetSessionStores', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('empties the per-user schema and language caches', () => {
    const schema = useSchemaStore()
    schema.collections = [{ name: 'a' } as never]
    schema.loaded = true
    schema.loadError = 'x'
    const lang = useLanguageStore()
    lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
    lang.loaded = true

    resetSessionStores()

    expect(schema.collections).toEqual([])
    expect(schema.loaded).toBe(false)
    expect(schema.loadError).toBe('')
    expect(lang.languages).toEqual([])
    expect(lang.loaded).toBe(false)
  })

  it('a schema load that was in flight before the reset cannot repopulate the store', async () => {
    let resolveFetch!: (v: never[]) => void
    vi.mocked(schemaApi.getAll).mockReturnValue(new Promise((r) => { resolveFetch = r as never }) as never)
    const schema = useSchemaStore()
    const pending = schema.load()
    resetSessionStores()
    resolveFetch([{ name: 'old-user-collection' }] as never)
    await pending
    expect(schema.collections).toEqual([])
    expect(schema.loaded).toBe(false)
  })

  it('rejects a pending confirm dialog', async () => {
    const confirm = useConfirmStore()
    const answer = confirm.ask({ message: 'm' })
    resetSessionStores()
    await expect(answer).resolves.toBe(false)
    expect(confirm.open).toBe(false)
  })

  it('leaves the theme, UI locale and public app config alone', () => {
    const theme = useThemeStore()
    const ui = useUiLocaleStore()
    const cfg = useAppConfigStore()
    cfg.brandName = 'Acme'
    const themeBefore = JSON.stringify(theme.$state)
    const uiBefore = JSON.stringify(ui.$state)
    resetSessionStores()
    expect(cfg.brandName).toBe('Acme')
    expect(JSON.stringify(theme.$state)).toBe(themeBefore)
    expect(JSON.stringify(ui.$state)).toBe(uiBefore)
  })
})
