import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSchemaStore } from '../stores/schemaStore'
import { useAuthStore } from '../stores/authStore'
import { useLanguageStore } from '../stores/languageStore'
import type { CollectionMeta } from '../types/schema'

const listMock = vi.fn()
vi.mock('../api/itemsApi', () => ({ itemsApi: { list: (...a: unknown[]) => listMock(...a) } }))

const coll = (name: string, defaultDisplayField: string | null = 'title'): CollectionMeta => ({
  name, label: name.toUpperCase(), fields: [
    { name: 'title', label: 'Title', interface: 'text', required: false, searchable: false, sortable: false, readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false },
  ], relations: [], defaultDisplayField,
})

async function run() {
  const { useDashboardData } = await import('./useDashboardData')
  const d = useDashboardData()
  await d.load()
  return d
}

beforeEach(() => {
  setActivePinia(createPinia())
  listMock.mockReset()
  const schema = useSchemaStore()
  schema.collections = [coll('article'), coll('category'), { name: 'file', label: 'File', fields: [], relations: [], defaultDisplayField: null }, { name: 'user', label: 'User', fields: [], relations: [], defaultDisplayField: null }]
  schema.loaded = true
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  lang.loaded = true
  const auth = useAuthStore()
  auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
})

describe('useDashboardData', () => {
  it('sums content totals and aggregates recent updates across content collections', async () => {
    listMock.mockImplementation((collection: string) => {
      if (collection === 'article') return Promise.resolve({ data: [{ id: 'a1', title: 'A1', updatedAt: '2026-07-14T00:00:00Z' }], total: 10 })
      if (collection === 'category') return Promise.resolve({ data: [{ id: 'c1', title: 'C1', updatedAt: '2026-07-15T00:00:00Z' }], total: 5 })
      return Promise.resolve({ data: [], total: 3 }) // file / user rows=1 count
    })
    const d = await run()
    expect(d.stats.value.items).toBe(15)
    expect(d.stats.value.collections).toBe(2)
    expect(d.stats.value.media).toBe(3)
    expect(d.stats.value.users).toBe(3)
    expect(d.recent.value.map((r) => r.id)).toEqual(['c1', 'a1']) // newest first
    expect(d.recent.value[0].collectionLabel).toBe('CATEGORY')
    expect(d.error.value).toBe('')
  })

  it('hides media/users stats when the user cannot read them', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u2', isSuperAdmin: false, permissions: { article: { read: true, write: false, delete: false }, category: { read: true, write: false, delete: false } } }
    listMock.mockResolvedValue({ data: [], total: 0 })
    const d = await run()
    expect(d.stats.value.media).toBeNull()
    expect(d.stats.value.users).toBeNull()
  })

  it('tolerates a single failing collection query (allSettled)', async () => {
    listMock.mockImplementation((collection: string) => {
      if (collection === 'article') return Promise.reject(new Error('boom'))
      return Promise.resolve({ data: [{ id: 'c1', title: 'C1', updatedAt: '2026-07-15T00:00:00Z' }], total: 5 })
    })
    const d = await run()
    expect(d.stats.value.items).toBe(5) // article dropped, category counted
    expect(d.recent.value.map((r) => r.id)).toEqual(['c1'])
    expect(d.error.value).toBe('')
  })
})
