import { defineStore } from 'pinia'
import { schemaApi } from '../api/schemaApi'
import { i18n } from '../i18n'
import type { CollectionMeta } from '../types/schema'

// Bumped by reset(): a load that started before the reset sees a different value when it settles
// and discards its result, so a previous session's data can never land in the next one.
let generation = 0

export const useSchemaStore = defineStore('schema', {
  state: () => ({
    collections: [] as CollectionMeta[],
    loaded: false,
    loadError: '',
    // In-flight fetch, shared by concurrent load() callers (e.g. AppShell.onMounted and a
    // deep-linked view's onMounted both calling load() before either has resolved) so a race
    // never triggers two schemaApi.getAll() requests.
    loadPromise: null as Promise<void> | null,
  }),
  actions: {
    // Forgets everything loaded for the previous user; the next load() fetches afresh.
    reset(): void {
      generation += 1
      this.collections = []
      this.loaded = false
      this.loadError = ''
      this.loadPromise = null
    },
    load(): Promise<void> {
      if (this.loaded) return Promise.resolve()
      if (this.loadPromise) return this.loadPromise
      const started = generation
      this.loadPromise = (async () => {
        try {
          const result = await schemaApi.getAll()
          if (started !== generation) return
          this.collections = result
          this.loaded = true
          this.loadError = ''
        } catch (e) {
          if (started !== generation) return
          this.loadError = e instanceof Error ? e.message : i18n.global.t('common.loadFailed')
        } finally {
          if (started === generation) this.loadPromise = null
        }
      })()
      return this.loadPromise
    },
    get(name: string): CollectionMeta | undefined {
      return this.collections.find((c) => c.name === name)
    },
  },
})
