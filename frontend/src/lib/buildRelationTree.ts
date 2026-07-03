export type LabeledRow = { id: string; label: string; [k: string]: unknown }
export type TreeNode = { key: string; label: string; data: string; children: TreeNode[] }

export function buildRelationTree(rows: LabeledRow[], parentKey: string, excludeId?: string): TreeNode[] {
  // Collect the excluded subtree (excludeId + all descendants) first.
  const excluded = new Set<string>()
  if (excludeId) {
    excluded.add(excludeId)
    let grew = true
    while (grew) {
      grew = false
      for (const r of rows) {
        const parent = r[parentKey]
        if (typeof parent === 'string' && excluded.has(parent) && !excluded.has(r.id)) {
          excluded.add(r.id)
          grew = true
        }
      }
    }
  }

  const usable = rows.filter((r) => !excluded.has(r.id))
  const byId = new Map<string, TreeNode>()
  for (const r of usable) byId.set(r.id, { key: r.id, label: r.label, data: r.id, children: [] })

  const roots: TreeNode[] = []
  for (const r of usable) {
    const node = byId.get(r.id)!
    const parent = r[parentKey]
    if (typeof parent === 'string' && byId.has(parent)) byId.get(parent)!.children.push(node)
    else roots.push(node)
  }
  return roots
}
