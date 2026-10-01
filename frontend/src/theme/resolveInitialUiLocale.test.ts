import { describe, it, expect, beforeEach } from 'vitest'
import { resolveInitialUiLocale, readSavedUiLocale, UI_LOCALE_STORAGE_KEY } from './resolveInitialUiLocale'

describe('resolveInitialUiLocale', () => {
  it('uses the saved locale when it is enabled', () => {
    expect(resolveInitialUiLocale('en', ['zh-TW', 'en'], 'zh-TW')).toBe('en')
  })
  it('falls back when nothing is saved', () => {
    expect(resolveInitialUiLocale(null, ['zh-TW', 'en'], 'zh-TW')).toBe('zh-TW')
  })
  it('falls back when the saved locale is not enabled', () => {
    expect(resolveInitialUiLocale('en', ['zh-TW'], 'zh-TW')).toBe('zh-TW')
    expect(resolveInitialUiLocale('ja', ['zh-TW', 'en'], 'en')).toBe('en')
  })
})

describe('readSavedUiLocale', () => {
  beforeEach(() => localStorage.clear())
  it('returns null when nothing is saved', () => {
    expect(readSavedUiLocale()).toBeNull()
  })
  it('returns the raw saved string', () => {
    localStorage.setItem(UI_LOCALE_STORAGE_KEY, 'en')
    expect(readSavedUiLocale()).toBe('en')
  })
})
