import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { schemaApi } from '../api/schemaApi'
import { i18n } from '../i18n'
import { createGuardedLoader } from '../lib/guardedLoad'
import { localizeCoreSchema } from '../lib/localizeCoreSchema'
import { catalogs, isUiLocale } from '../locales'
import type { CollectionMeta } from '../types/schema'

// reset() invalidates a load still in flight, so a previous session's data can never land in the
// next one.
const loader = createGuardedLoader()

export const useSchemaStore = defineStore('schema', () => {
  // The collections exactly as the server sent them.
  const raw = ref<CollectionMeta[]>([])
  const loaded = ref(false)
  const loadError = ref('')
  // In-flight fetch, shared by concurrent load() callers (e.g. AppShell.onMounted and a
  // deep-linked view's onMounted both calling load() before either has resolved) so a race
  // never triggers two schemaApi.getAll() requests.
  const loadPromise = ref<Promise<void> | null>(null)

  // Core collection labels in the current UI locale; reading follows a locale switch, and
  // assigning replaces the raw data.
  const collections = computed<CollectionMeta[], CollectionMeta[]>({
    get() {
      const locale = i18n.global.locale.value
      if (!isUiLocale(locale)) return raw.value
      return localizeCoreSchema(raw.value, { source: catalogs.en.core, target: catalogs[locale].core })
    },
    set(value) {
      raw.value = value
    },
  })

  // Forgets everything loaded for the previous user; the next load() fetches afresh.
  function reset(): void {
    loader.invalidate()
    raw.value = []
    loaded.value = false
    loadError.value = ''
    loadPromise.value = null
  }

  function load(): Promise<void> {
    if (loaded.value) return Promise.resolve()
    if (loadPromise.value) return loadPromise.value
    loadPromise.value = loader.run(() => schemaApi.getAll(), {
      apply: (result) => {
        raw.value = result
        loaded.value = true
        loadError.value = ''
      },
      fail: (e) => {
        loadError.value = e instanceof Error ? e.message : i18n.global.t('common.loadFailed')
      },
      settle: () => {
        loadPromise.value = null
      },
    })
    return loadPromise.value
  }

  function get(name: string): CollectionMeta | undefined {
    return collections.value.find((c) => c.name === name)
  }

  return { raw, collections, loaded, loadError, loadPromise, reset, load, get }
})
