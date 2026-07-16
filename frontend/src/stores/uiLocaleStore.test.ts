import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useUiLocaleStore } from './uiLocaleStore'
import { i18n } from '../i18n'

describe('uiLocaleStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    i18n.global.locale.value = 'zh-TW'
    document.documentElement.removeAttribute('lang')
  })

  it('set updates the i18n locale, <html lang>, and persists', () => {
    const s = useUiLocaleStore()
    s.set('en')
    expect(s.locale).toBe('en')
    expect(i18n.global.locale.value).toBe('en')
    expect(document.documentElement.getAttribute('lang')).toBe('en')
    expect(localStorage.getItem('struo.uiLocale')).toBe('en')
  })
})
