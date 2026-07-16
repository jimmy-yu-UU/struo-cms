import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useThemeStore } from './themeStore'

describe('themeStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    document.documentElement.classList.remove('app-dark')
  })

  it('toggle switches mode, applies the class, and persists', () => {
    const s = useThemeStore()
    s.set('light')
    expect(s.isDark).toBe(false)
    expect(document.documentElement.classList.contains('app-dark')).toBe(false)
    s.toggle()
    expect(s.mode).toBe('dark')
    expect(s.isDark).toBe(true)
    expect(document.documentElement.classList.contains('app-dark')).toBe(true)
    expect(localStorage.getItem('struo.theme')).toBe('dark')
  })

  it('set applies immediately and persists', () => {
    const s = useThemeStore()
    s.set('dark')
    expect(document.documentElement.classList.contains('app-dark')).toBe(true)
    expect(localStorage.getItem('struo.theme')).toBe('dark')
    s.set('light')
    expect(document.documentElement.classList.contains('app-dark')).toBe(false)
    expect(localStorage.getItem('struo.theme')).toBe('light')
  })
})
