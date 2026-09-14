// Pure helpers for shaping a flat list of chapter entries into VitePress
// sidebar items, with the pre-rewrite ("legacy") chapters folded into one
// collapsed trailing group instead of being interleaved with the rewritten
// ones. No I/O here — callers (docs/.vitepress/sidebar.mts) own reading the
// filesystem; these functions only rearrange data they are handed.

/**
 * @typedef {{ file: string, text: string, link: string }} ChapterEntry
 * @typedef {{ text: string, link: string }} SidebarLink
 * @typedef {{ text: string, collapsed: true, items: SidebarLink[] }} SidebarGroup
 * @typedef {SidebarLink | SidebarGroup} SidebarItem
 */

// entries is assumed already sorted (chapter order); this function preserves
// that order within each of the two buckets it produces.
/**
 * @param {ChapterEntry[]} entries
 * @param {Set<string>} legacyFiles
 * @param {string} legacyLabel
 * @returns {SidebarItem[]}
 */
export function groupChapters(entries, legacyFiles, legacyLabel) {
  const current = []
  const legacy = []

  for (const entry of entries) {
    const target = legacyFiles.has(entry.file) ? legacy : current
    target.push({ text: entry.text, link: entry.link })
  }

  const items = [...current]
  if (legacy.length > 0) {
    items.push({ text: legacyLabel, collapsed: true, items: legacy })
  }
  return items
}

// Descends into the first item, and into that item's `items` group if it has
// one, until it finds a link. Throws rather than returning undefined: an
// empty or link-less sidebar is a config bug, not a value callers should have
// to null-check.
/**
 * @param {SidebarItem[]} items
 * @returns {string}
 */
export function firstLink(items) {
  const [first] = items
  if (!first) {
    throw new Error('firstLink: no items, so there is no link')
  }
  if ('link' in first && first.link) {
    return first.link
  }
  if ('items' in first && Array.isArray(first.items)) {
    return firstLink(first.items)
  }
  throw new Error('firstLink: first item has neither a link nor nested items')
}
