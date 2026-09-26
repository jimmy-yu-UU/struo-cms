import { describe, it, expect, vi, afterEach } from 'vitest'
import { applyUiLocaleConfig } from './uiLocaleConfig'

describe('applyUiLocaleConfig', () => {
  afterEach(() => vi.restoreAllMocks())

  it('keeps the server order and default when every code is a bundled catalog', () => {
    expect(applyUiLocaleConfig(['en', 'zh-TW'], 'en')).toEqual({ enabled: ['en', 'zh-TW'], defaultLocale: 'en' })
  })
  it('drops codes that are not bundled catalogs', () => {
    const err = vi.spyOn(console, 'error').mockImplementation(() => {})
    expect(applyUiLocaleConfig(['zh-TW', 'ja'], 'zh-TW')).toEqual({ enabled: ['zh-TW'], defaultLocale: 'zh-TW' })
    expect(err).toHaveBeenCalledOnce()
  })
  it('falls back to every bundled catalog when nothing survives', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const r = applyUiLocaleConfig(['ja'], 'ja')
    expect([...r.enabled].sort()).toEqual(['en', 'zh-TW'])
    expect(r.enabled).toContain(r.defaultLocale)
  })
  it('moves the default onto the first enabled locale when it is not enabled', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    expect(applyUiLocaleConfig(['en'], 'zh-TW')).toEqual({ enabled: ['en'], defaultLocale: 'en' })
  })
})
