import { defineStore } from 'pinia'
import { schemaApi } from '../api/schemaApi'
import { i18n } from '../i18n'
import { createGuardedLoader } from '../lib/guardedLoad'
import type { CollectionMeta } from '../types/schema'

// reset() invalidates a load still in flight, so a previous session's data can never land in the
// next one.
const loader = createGuardedLoader()

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
      loader.invalidate()
      this.collections = []
      this.loaded = false
      this.loadError = ''
      this.loadPromise = null
    },
    load(): Promise<void> {
      if (this.loaded) return Promise.resolve()
      if (this.loadPromise) return this.loadPromise
      this.loadPromise = loader.run(() => schemaApi.getAll(), {
        apply: (result) => {
          this.collections = result
          this.loaded = true
          this.loadError = ''
        },
        fail: (e) => {
          this.loadError = e instanceof Error ? e.message : i18n.global.t('common.loadFailed')
        },
        settle: () => {
          this.loadPromise = null
        },
      })
      return this.loadPromise
    },
    get(name: string): CollectionMeta | undefined {
      return this.collections.find((c) => c.name === name)
    },
  },
})
