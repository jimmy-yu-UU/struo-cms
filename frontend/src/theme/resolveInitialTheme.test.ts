import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { resolveInitialTheme } from './resolveInitialTheme'

describe('resolveInitialTheme', () => {
  beforeEach(() => { localStorage.clear() })
  afterEach(() => { vi.restoreAllMocks() })

  it('honours a saved dark preference', () => {
    localStorage.setItem('struo.theme', 'dark')
    expect(resolveInitialTheme()).toBe('dark')
  })

  it('honours a saved light preference', () => {
    localStorage.setItem('struo.theme', 'light')
    expect(resolveInitialTheme()).toBe('light')
  })

  it('falls back to system dark when nothing is saved', () => {
    vi.spyOn(window, 'matchMedia').mockReturnValue({ matches: true } as MediaQueryList)
    expect(resolveInitialTheme()).toBe('dark')
  })

  it('defaults to light when nothing saved and system is light', () => {
    // global vitest.setup stub returns matches:false
    expect(resolveInitialTheme()).toBe('light')
  })

  it('ignores an unknown saved value', () => {
    localStorage.setItem('struo.theme', 'weird')
    expect(resolveInitialTheme()).toBe('light')
  })
})
