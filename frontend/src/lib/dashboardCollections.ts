import type { CollectionMeta } from '../types/schema'

/**
 * Collections the dashboard treats as system: each gets its own stat card and is excluded from the
 * content items total / collections count / recent updates. Note this is stricter than `buildNav`,
 * which only hardcodes-excludes 'file' — the dashboard additionally excludes 'user' so the users card
 * does not double-count into the items total.
 */
export const SYSTEM_COLLECTIONS = ['file', 'user'] as const

export function contentCollections(
  collections: CollectionMeta[],
  canRead: (name: string) => boolean,
): CollectionMeta[] {
  const system: readonly string[] = SYSTEM_COLLECTIONS
  return collections.filter((c) => !system.includes(c.name) && canRead(c.name))
}
