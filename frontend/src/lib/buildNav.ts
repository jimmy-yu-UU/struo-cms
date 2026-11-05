import type { CollectionMeta, CollectionPermission } from '../types/schema'

// `route` names a router route for an item that is not a collection; without it the item opens the
// collection's list.
export type NavItem = { name: string; label: string; icon?: string | null; route?: string }
export type NavGroup = { group: string; items: NavItem[] }
// A non-collection item added to a group, shown only to a caller who holds `capability`.
export type NavExtra = { group: string; capability: string; item: NavItem }

const UNGROUPED = 'General'

export function buildNav(
  collections: CollectionMeta[],
  isSuperAdmin: boolean,
  permissions: Record<string, CollectionPermission>,
  extras: NavExtra[] = [],
  hasCapability: (code: string) => boolean = () => false,
): NavGroup[] {
  const readable = collections.filter(
    // Visibility is metadata-driven (CmsCollection Hidden flag) — no hardcoded names.
    (c) => !c.hidden && (isSuperAdmin || permissions[c.name]?.read === true),
  )

  const groups = new Map<string, NavItem[]>()
  for (const c of readable) {
    const key = c.group && c.group.trim() !== '' ? c.group : UNGROUPED
    if (!groups.has(key)) groups.set(key, [])
    groups.get(key)!.push({ name: c.name, label: c.label, icon: c.icon })
  }

  for (const e of extras) {
    if (!hasCapability(e.capability)) continue
    if (!groups.has(e.group)) groups.set(e.group, [])
    groups.get(e.group)!.push(e.item)
  }

  return Array.from(groups.entries()).map(([group, items]) => ({ group, items }))
}
