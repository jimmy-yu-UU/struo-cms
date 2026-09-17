// Pure helper for reading a VitePress sidebar's first link. No I/O here —
// the caller (docs/.vitepress/config.mts) hands it the sidebar that
// docs/.vitepress/sidebar.mts built; this function only inspects that data.
// It stays generic over a nested `items` group (rather than assuming a flat
// list) so a locale's first link can still be found if a fork later
// reintroduces a collapsed group of its own.

/**
 * @typedef {{ text: string, link: string }} SidebarLink
 * @typedef {{ text: string, items: SidebarLink[], collapsed?: boolean }} SidebarGroup
 * @typedef {SidebarLink | SidebarGroup} SidebarItem
 */

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
