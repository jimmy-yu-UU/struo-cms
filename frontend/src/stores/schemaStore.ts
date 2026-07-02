import { defineStore } from 'pinia'
import { schemaApi } from '../api/schemaApi'
import type { CollectionMeta } from '../types/schema'

export const useSchemaStore = defineStore('schema', {
  state: () => ({
    collections: [] as CollectionMeta[],
    loaded: false,
    loadError: '',
  }),
  actions: {
    async load(): Promise<void> {
      if (this.loaded) return
      try {
        this.collections = await schemaApi.getAll()
        this.loaded = true
        this.loadError = ''
      } catch (e) {
        this.loadError = e instanceof Error ? e.message : 'Failed to load schema.'
      }
    },
    get(name: string): CollectionMeta | undefined {
      return this.collections.find((c) => c.name === name)
    },
  },
})
