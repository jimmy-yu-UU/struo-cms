export type ThemeMode = 'light' | 'dark'

export function resolveInitialTheme(): ThemeMode {
  try {
    const saved = localStorage.getItem('struo.theme')
    if (saved === 'dark' || saved === 'light') return saved
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
    }
  } catch {
    /* localStorage/matchMedia unavailable — fall through to the default */
  }
  return 'light'
}
