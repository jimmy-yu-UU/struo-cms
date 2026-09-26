import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useUiLocaleStore } from './uiLocaleStore'
import { i18n } from '../i18n'

describe('uiLocaleStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    i18n.global.locale.value = 'zh-TW'
    i18n.global.fallbackLocale.value = 'zh-TW'
    document.documentElement.removeAttribute('lang')
  })

  it('set updates the i18n locale, <html lang>, and persists', () => {
    const s = useUiLocaleStore()
    s.configure(['zh-TW', 'en'], 'zh-TW')
    expect(s.set('en')).toBe(true)
    expect(s.locale).toBe('en')
    expect(i18n.global.locale.value).toBe('en')
    expect(document.documentElement.getAttribute('lang')).toBe('en')
    expect(localStorage.getItem('struo.uiLocale')).toBe('en')
  })

  it('configure applies the saved locale when enabled, else the default, and sets the fallback', () => {
    localStorage.setItem('struo.uiLocale', 'en')
    const s = useUiLocaleStore()
    s.configure(['zh-TW', 'en'], 'zh-TW')
    expect(s.locale).toBe('en')
    expect(i18n.global.fallbackLocale.value).toBe('zh-TW')

    const t = useUiLocaleStore()
    t.configure(['zh-TW'], 'zh-TW')
    expect(t.locale).toBe('zh-TW')
    expect(i18n.global.locale.value).toBe('zh-TW')
  })

  it('set refuses a locale that is not enabled', () => {
    const s = useUiLocaleStore()
    s.configure(['zh-TW'], 'zh-TW')
    expect(s.set('en')).toBe(false)
    expect(s.locale).toBe('zh-TW')
    expect(localStorage.getItem('struo.uiLocale')).toBeNull()
  })
})
