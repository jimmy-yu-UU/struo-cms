import { defineStore } from 'pinia'
import { resolveInitialTheme, type ThemeMode } from '../theme/resolveInitialTheme'

export const useThemeStore = defineStore('theme', {
  state: () => ({ mode: resolveInitialTheme() as ThemeMode }),
  getters: {
    isDark: (state): boolean => state.mode === 'dark',
  },
  actions: {
    apply(): void {
      document.documentElement.classList.toggle('app-dark', this.mode === 'dark')
      try {
        localStorage.setItem('struo.theme', this.mode)
      } catch {
        /* localStorage unavailable — class is still applied in-memory */
      }
    },
    set(mode: ThemeMode): void {
      this.mode = mode
      this.apply()
    },
    toggle(): void {
      this.set(this.mode === 'dark' ? 'light' : 'dark')
    },
  },
})
