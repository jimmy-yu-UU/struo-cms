import type { CollectionMeta } from '../types/schema'
import { SYSTEM_COLLECTIONS } from './dashboardCollections'

export type QuickAction =
  | { kind: 'uploadMedia' }
  | { kind: 'newItem'; collection: string; label: string }

export function buildQuickActions(
  collections: CollectionMeta[],
  canWrite: (name: string) => boolean,
  maxNewItems: number,
): QuickAction[] {
  const actions: QuickAction[] = []
  if (canWrite('file')) actions.push({ kind: 'uploadMedia' })

  const system: readonly string[] = SYSTEM_COLLECTIONS
  const writableContent = collections.filter((c) => !system.includes(c.name) && canWrite(c.name))
  for (const c of writableContent.slice(0, maxNewItems)) {
    actions.push({ kind: 'newItem', collection: c.name, label: c.label })
  }
  return actions
}
