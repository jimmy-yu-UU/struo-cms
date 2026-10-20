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
    s.init()
    expect(s.set('zh-TW')).toBe(true)
    expect(s.locale).toBe('zh-TW')
    expect(i18n.global.locale.value).toBe('zh-TW')
    expect(document.documentElement.getAttribute('lang')).toBe('zh-TW')
    expect(localStorage.getItem('struo.uiLocale')).toBe('zh-TW')
  })

  it('init applies the build default (en) and sets <html lang> when nothing is saved', () => {
    const s = useUiLocaleStore()
    s.init()
    expect(s.locale).toBe('en')
    expect(i18n.global.locale.value).toBe('en')
    expect(document.documentElement.getAttribute('lang')).toBe('en')
  })

  it('init lets a saved choice win over the default', () => {
    localStorage.setItem('struo.uiLocale', 'zh-TW')
    const s = useUiLocaleStore()
    s.init()
    expect(s.locale).toBe('zh-TW')
    expect(document.documentElement.getAttribute('lang')).toBe('zh-TW')
  })

  it('init ignores a saved locale that has no catalog', () => {
    localStorage.setItem('struo.uiLocale', 'ja')
    const s = useUiLocaleStore()
    s.init()
    expect(s.locale).toBe('en')
  })

  it('set refuses a locale that is not offered', () => {
    const s = useUiLocaleStore()
    s.init()
    expect(s.set('ja' as never)).toBe(false)
    expect(s.locale).toBe('en')
    expect(localStorage.getItem('struo.uiLocale')).toBeNull()
  })

  it('every registered catalog is offered', () => {
    expect([...useUiLocaleStore().enabled].sort()).toEqual(['en', 'zh-TW'])
  })
})
