import { describe, it, expect, vi, afterEach } from 'vitest'
import { resolveDefaultUiLocale } from './uiLocaleConfig'
import { CATALOG_LOCALES } from '../locales'

describe('resolveDefaultUiLocale', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllEnvs()
    vi.resetModules()
  })

  it('offers exactly the registered catalogs', () => {
    expect([...CATALOG_LOCALES].sort()).toEqual(['en', 'zh-TW'])
  })
  it('defaults to en when nothing is configured', () => {
    expect(resolveDefaultUiLocale(undefined)).toBe('en')
    expect(resolveDefaultUiLocale('')).toBe('en')
  })
  it('honours a registered override, case-insensitively', () => {
    expect(resolveDefaultUiLocale('zh-TW')).toBe('zh-TW')
    expect(resolveDefaultUiLocale('zh-tw')).toBe('zh-TW')
  })
  it('falls back to en with a warning for an unknown value', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    expect(resolveDefaultUiLocale('ja')).toBe('en')
    expect(warn).toHaveBeenCalledOnce()
  })
  it('reads VITE_DEFAULT_UI_LOCALE at module load', async () => {
    vi.stubEnv('VITE_DEFAULT_UI_LOCALE', 'zh-TW')
    vi.resetModules()
    const mod = await import('./uiLocaleConfig')
    expect(mod.DEFAULT_UI_LOCALE).toBe('zh-TW')
  })
})
