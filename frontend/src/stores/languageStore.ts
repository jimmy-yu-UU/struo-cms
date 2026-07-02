import { defineStore } from 'pinia'
import { languagesApi } from '../api/languagesApi'
import type { LanguageInfo } from '../types/schema'

export const useLanguageStore = defineStore('language', {
  state: () => ({
    languages: [] as LanguageInfo[],
    loaded: false,
    loadError: '',
  }),
  getters: {
    defaultCode: (state): string =>
      state.languages.find((l) => l.isDefault)?.code ?? state.languages[0]?.code ?? '',
  },
  actions: {
    async load(): Promise<void> {
      if (this.loaded) return
      try {
        this.languages = await languagesApi.getEnabled()
        this.loaded = true
        this.loadError = ''
      } catch (e) {
        this.loadError = e instanceof Error ? e.message : 'Failed to load languages.'
      }
    },
  },
})
