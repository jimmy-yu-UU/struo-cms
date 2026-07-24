import type { CollectionMeta, CollectionPermission } from '../types/schema'

export type NavItem = { name: string; label: string; icon?: string | null }
export type NavGroup = { group: string; items: NavItem[] }

const UNGROUPED = 'General'

export function buildNav(
  collections: CollectionMeta[],
  isSuperAdmin: boolean,
  permissions: Record<string, CollectionPermission>,
): NavGroup[] {
  const readable = collections.filter(
    // Batch B: visibility is metadata-driven (CmsCollection Hidden flag) — no hardcoded names.
    (c) => !c.hidden && (isSuperAdmin || permissions[c.name]?.read === true),
  )

  const groups = new Map<string, NavItem[]>()
  for (const c of readable) {
    const key = c.group && c.group.trim() !== '' ? c.group : UNGROUPED
    if (!groups.has(key)) groups.set(key, [])
    groups.get(key)!.push({ name: c.name, label: c.label, icon: c.icon })
  }

  return Array.from(groups.entries()).map(([group, items]) => ({ group, items }))
}
