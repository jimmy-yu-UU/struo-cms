export type FolderRow = { id: string; name: string; parentId: string | null; version?: number }

export function toFolderRows(data: Record<string, unknown>[]): FolderRow[] {
  return data.map((r) => ({
    id: String(r.id ?? ''),
    name: typeof r.name === 'string' ? r.name : '',
    // The items API only projects M2O relation FKs (mediafolder.parentId) under `deep`
    // expansion, nested as `parent: { id, ... }` under the relation's nav-property name --
    // it never returns a flat `parentId` column. Prefer the deep-expanded nested shape, but
    // fall back to a literal parentId for any caller still passing one directly (e.g. tests).
    parentId: (r.parent as { id?: string } | undefined)?.id
      ?? (typeof r.parentId === 'string' ? r.parentId : null),
    version: typeof r.version === 'number' ? r.version : undefined,
  }))
}

export function childFolders(folders: FolderRow[], parentId: string | null): FolderRow[] {
  return folders.filter((f) => f.parentId === parentId)
}

/** Breadcrumb ancestor chain, root first, ending at `id`. Cycle-safe (stops on revisit). */
export function folderPath(folders: FolderRow[], id: string | null): FolderRow[] {
  if (!id) return []
  const byId = new Map(folders.map((f) => [f.id, f]))
  const path: FolderRow[] = []
  const seen = new Set<string>()
  let cur = byId.get(id)
  while (cur && !seen.has(cur.id)) {
    seen.add(cur.id)
    path.unshift(cur)
    cur = cur.parentId ? byId.get(cur.parentId) : undefined
  }
  return path
}
