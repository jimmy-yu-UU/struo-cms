// A monotonic token factory for the "latest response wins" pattern.
//
// Async loaders race: a slow earlier request can resolve after a faster later
// one and clobber fresh state with stale data. Guard each loader by taking a
// token on entry and, before every state write after an `await`, bailing out if
// the token is no longer current.
//
//   const t = lw.next()
//   const res = await api.load()
//   if (!lw.isCurrent(t)) return   // a newer load superseded this one
//   state.value = res
export function createLatestWins(): { next(): number; isCurrent(token: number): boolean } {
  let current = 0
  return {
    next(): number {
      current += 1
      return current
    },
    isCurrent(token: number): boolean {
      return token === current
    },
  }
}
