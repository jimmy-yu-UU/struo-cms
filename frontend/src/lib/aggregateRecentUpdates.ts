export type RecentRow = {
  id: string
  collection: string
  collectionLabel: string
  title: string
  updatedAt: string | null
}

function timestamp(iso: string | null): number {
  if (!iso) return Number.NEGATIVE_INFINITY
  const t = Date.parse(iso)
  return Number.isNaN(t) ? Number.NEGATIVE_INFINITY : t
}

/** Merge already-flattened rows, newest first; rows with null/invalid updatedAt sort last. */
export function aggregateRecentUpdates(rows: RecentRow[], limit: number): RecentRow[] {
  return [...rows].sort((a, b) => timestamp(b.updatedAt) - timestamp(a.updatedAt)).slice(0, limit)
}
