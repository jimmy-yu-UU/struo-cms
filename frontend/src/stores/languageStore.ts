import { defineStore } from 'pinia'
import { languagesApi } from '../api/languagesApi'
import { i18n } from '../i18n'
import type { LanguageInfo } from '../types/schema'

export const useLanguageStore = defineStore('language', {
  state: () => ({
    languages: [] as LanguageInfo[],
    loaded: false,
    loadError: '',
    // In-flight fetch, shared by concurrent load() callers (e.g. MediaLibraryView's own load()
    // and MediaDetailDialog's load(), both awaiting Promise.all([schema.load(), langStore.load()])
    // when a file is opened before the library's initial load has resolved) so a race never
    // triggers two languagesApi.getEnabled() requests (mirrors schemaStore's loadPromise pattern).
    loadPromise: null as Promise<void> | null,
  }),
  getters: {
    defaultCode: (state): string =>
      state.languages.find((l) => l.isDefault)?.code ?? state.languages[0]?.code ?? '',
  },
  actions: {
    load(): Promise<void> {
      if (this.loaded) return Promise.resolve()
      if (this.loadPromise) return this.loadPromise
      this.loadPromise = (async () => {
        try {
          this.languages = await languagesApi.getEnabled()
          this.loaded = true
          this.loadError = ''
        } catch (e) {
          this.loadError = e instanceof Error ? e.message : i18n.global.t('common.loadFailed')
        } finally {
          this.loadPromise = null
        }
      })()
      return this.loadPromise
    },
    // The Language collection is edited through the generic item form; after such a save the
    // enabled-languages snapshot is stale (new locale tabs won't appear until refetched).
    async reload(): Promise<void> {
      if (this.loadPromise) await this.loadPromise // let an in-flight load settle before resetting
      this.loaded = false
      await this.load()
    },
  },
})
