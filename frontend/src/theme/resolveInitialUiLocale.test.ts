import { describe, it, expect, beforeEach } from 'vitest'
import { resolveInitialUiLocale, DEFAULT_UI_LOCALE } from './resolveInitialUiLocale'

describe('resolveInitialUiLocale', () => {
  beforeEach(() => { localStorage.clear() })

  it('defaults to zh-TW', () => {
    expect(DEFAULT_UI_LOCALE).toBe('zh-TW')
    expect(resolveInitialUiLocale()).toBe('zh-TW')
  })

  it('honours a saved en preference', () => {
    localStorage.setItem('struo.uiLocale', 'en')
    expect(resolveInitialUiLocale()).toBe('en')
  })

  it('honours a saved zh-TW preference', () => {
    localStorage.setItem('struo.uiLocale', 'zh-TW')
    expect(resolveInitialUiLocale()).toBe('zh-TW')
  })

  it('ignores an unknown saved value', () => {
    localStorage.setItem('struo.uiLocale', 'ja')
    expect(resolveInitialUiLocale()).toBe('zh-TW')
  })
})
