import { defineStore } from 'pinia'
import { schemaApi } from '../api/schemaApi'
import type { CollectionMeta } from '../types/schema'

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
    load(): Promise<void> {
      if (this.loaded) return Promise.resolve()
      if (this.loadPromise) return this.loadPromise
      this.loadPromise = (async () => {
        try {
          this.collections = await schemaApi.getAll()
          this.loaded = true
          this.loadError = ''
        } catch (e) {
          this.loadError = e instanceof Error ? e.message : 'Failed to load schema.'
        } finally {
          this.loadPromise = null
        }
      })()
      return this.loadPromise
    },
    get(name: string): CollectionMeta | undefined {
      return this.collections.find((c) => c.name === name)
    },
  },
})
