import { ref, type Ref } from 'vue'
import { itemsApi } from '../api/itemsApi'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { contentCollections } from '../lib/dashboardCollections'
import { resolveItemTitle } from '../lib/resolveItemTitle'
import { aggregateRecentUpdates, type RecentRow } from '../lib/aggregateRecentUpdates'
import { buildQuickActions, type QuickAction } from '../lib/quickActions'
import { resolveGreetingKey } from '../lib/greeting'
import type { CollectionMeta } from '../types/schema'

const RECENT_LIMIT = 8
const MAX_QUICK_NEW_ITEMS = 3

export type DashboardStats = {
  items: number
  collections: number
  media: number | null
  users: number | null
}

export type UseDashboardData = {
  stats: Ref<DashboardStats>
  recent: Ref<RecentRow[]>
  quickActions: Ref<QuickAction[]>
  greetingKey: Ref<'morning' | 'afternoon' | 'evening'>
  loading: Ref<boolean>
  error: Ref<string>
  load: () => Promise<void>
}

export function useDashboardData(): UseDashboardData {
  const auth = useAuthStore()
  const schema = useSchemaStore()
  const lang = useLanguageStore()

  const stats = ref<DashboardStats>({ items: 0, collections: 0, media: null, users: null })
  const recent = ref<RecentRow[]>([])
  const quickActions = ref<QuickAction[]>([])
  const greetingKey = ref<'morning' | 'afternoon' | 'evening'>('morning')
  const loading = ref(false)
  const error = ref('')

  async function countOf(collection: string, locale: string | undefined): Promise<number | null> {
    if (!auth.canRead(collection)) return null
    try {
      const res = await itemsApi.list(collection, { page: 0, rows: 1, locale })
      return res.total
    } catch {
      return null
    }
  }

  async function load(): Promise<void> {
    loading.value = true
    error.value = ''
    try {
      await Promise.all([schema.load(), lang.load()])
      const locale = lang.defaultCode || undefined
      const content: CollectionMeta[] = contentCollections(schema.collections, auth.canRead)

      const settled = await Promise.allSettled(
        content.map((c) =>
          itemsApi
            .list(c.name, { page: 0, rows: RECENT_LIMIT, sort: '-updatedAt', locale })
            .then((res) => ({ meta: c, res })),
        ),
      )

      let itemsTotal = 0
      const rows: RecentRow[] = []
      let anyFulfilled = false
      for (const s of settled) {
        if (s.status !== 'fulfilled') continue
        anyFulfilled = true
        const { meta, res } = s.value
        itemsTotal += res.total
        for (const row of res.data) {
          rows.push({
            id: String(row.id),
            collection: meta.name,
            collectionLabel: meta.label,
            title: resolveItemTitle(row, meta, locale ?? ''),
            updatedAt: typeof row.updatedAt === 'string' ? row.updatedAt : null,
          })
        }
      }

      const [media, users] = await Promise.all([countOf('file', locale), countOf('user', locale)])

      stats.value = { items: itemsTotal, collections: content.length, media, users }
      recent.value = aggregateRecentUpdates(rows, RECENT_LIMIT)
      quickActions.value = buildQuickActions(schema.collections, auth.canWrite, MAX_QUICK_NEW_ITEMS)
      greetingKey.value = resolveGreetingKey(new Date().getHours())

      if (content.length > 0 && !anyFulfilled) error.value = 'error'
    } catch {
      error.value = 'error'
    } finally {
      loading.value = false
    }
  }

  return { stats, recent, quickActions, greetingKey, loading, error, load }
}
