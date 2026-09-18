import type { Router } from 'vue-router'

// Routed views and the richText field are lazy chunks (`() => import()` / `defineAsyncComponent`).
// After a redeploy, a tab still holding the old index.html asks for a hashed chunk filename that
// no longer exists on the server. Two independent Vite failure paths can fire for that: narrative-guard:allow: describes the stale-chunk 404 scenario, not code history
//   - A router-driven navigation: vue-router's dynamic `import()` rejects and reaches
//     `router.onError`. Vite's own preload helper also dispatches `vite:preloadError` for the
//     same failure and then RETHROWS unless `preventDefault()` is called, so both handlers see
//     one failure. While a navigation is in flight, `onError` is treated as the owner and the
//     `vite:preloadError` listener is a no-op, so exactly one recovery action happens.
//   - An async component load outside any navigation (e.g. the richText field, or a preloaded
//     eager chunk): only `vite:preloadError` fires, so that listener owns recovery for this case.
// Recovery is a single reload of the current URL, which re-fetches index.html and its up-to-date
// manifest. A sessionStorage guard (one slot, keyed by the URL being reloaded) allows only one
// reload per URL: if the chunk is genuinely gone (a bad deploy, not a stale tab) this stops an
// infinite reload loop and lets the failure surface as an error instead. Any navigation that
// completes successfully clears the guard, so the *next* redeploy in this tab can recover again
// instead of being silently blocked by a stale guard entry from an earlier, already-resolved
// failure.

const CHUNK_LOAD_ERROR = /Failed to fetch dynamically imported module|error loading dynamically imported module|Importing a module script failed/i
const RELOAD_GUARD_KEY = 'struo.chunkReload'

export function isChunkLoadError(err: unknown): boolean {
  const message = err instanceof Error ? err.message : String(err)
  return CHUNK_LOAD_ERROR.test(message)
}

export interface RecoveryHost {
  location: { assign(url: string): void; reload(): void; pathname: string; search: string }
  addEventListener(type: string, listener: () => void): void
  sessionStorage: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>
}

export function installChunkLoadRecovery(router: Router, host: RecoveryHost = window): void {
  let navigating = false
  router.beforeEach(() => { navigating = true })
  // Any completed navigation proves the current manifest is loadable: forget the guard so the
  // NEXT redeploy in this tab can recover again.
  router.afterEach(() => { navigating = false; clearGuard(host) })
  router.onError((err, to) => {
    navigating = false
    if (!isChunkLoadError(err)) return
    if (armReload(host, to.fullPath)) host.location.assign(to.fullPath)
  })
  // Fires for every failed dynamic import/preload. While a router navigation is in flight the
  // router's onError owns the failure (Vite rethrows to it), so do nothing here; otherwise
  // (an async component such as the richText editor, or a preloaded eager chunk) reload once.
  host.addEventListener('vite:preloadError', () => {
    if (navigating) return
    const key = host.location.pathname + host.location.search
    if (armReload(host, key)) host.location.reload()
  })
}

// One reload per failing key per tab session: a chunk that is genuinely gone (bad deploy) must
// surface as an error, not as an infinite reload loop.
function armReload(host: RecoveryHost, key: string): boolean {
  try {
    if (host.sessionStorage.getItem(RELOAD_GUARD_KEY) === key) {
      host.sessionStorage.removeItem(RELOAD_GUARD_KEY)
      return false
    }
    host.sessionStorage.setItem(RELOAD_GUARD_KEY, key)
    return true
  } catch {
    return true
  }
}

function clearGuard(host: RecoveryHost): void {
  try {
    host.sessionStorage.removeItem(RELOAD_GUARD_KEY)
  } catch {
    // sessionStorage inaccessible (private browsing, etc.) — nothing to clear.
  }
}
