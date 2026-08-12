// Pure reordering helper for SortableList.vue, split out so its bounds guard is testable without
// mounting a component. Guards all four bounds -- `from`'s lower AND upper, `to`'s lower AND
// upper -- not just `to`: an out-of-range `from` would make `splice(from, 1)` return `[]`, and the
// following `splice(to, 0, item)` would then insert `undefined` into the result. That is a worse
// failure than simply refusing the move, and `to` alone being in range does not make `from` safe.
// `to`'s own bounds are unreachable through this app's only caller today (SortableList.vue only
// calls this from buttons that are themselves disabled at the edges), but a future caller that
// derives `from`/`to` from something other than a disabled-gated button -- a drag interaction
// computing indices from pointer geometry, for instance -- would hit either bound with no button
// there to have refused the click first.
//
// Returns the SAME array reference when the move is out of bounds, so a caller can cheaply tell
// "did anything change" with `!==`. Returns a NEW array whenever the move is valid and never
// mutates `list` -- the caller always receives a distinct array, so no consumer can observe the
// old and the new order as the same object. (`canWrite`, not dirtiness, gates Save; this is about
// not leaking a shared reference, not about dirty-tracking.)
export function reorder<T>(list: T[], from: number, to: number): T[] {
  if (from < 0 || from >= list.length || to < 0 || to >= list.length) return list
  const next = [...list]
  const [item] = next.splice(from, 1)
  next.splice(to, 0, item)
  return next
}
