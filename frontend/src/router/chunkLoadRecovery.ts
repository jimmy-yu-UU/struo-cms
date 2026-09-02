import type { Router } from 'vue-router'

// Routed views and the richText field are lazy chunks (`() => import()` / `defineAsyncComponent`).
// After a redeploy, a tab still holding the old index.html asks for a hashed chunk filename that
// no longer exists on the server; vue-router rejects the navigation with only a console error and
// the async field renders nothing, with no feedback to the user. This module recovers from that:
// on the first sighting of a chunk-load failure for a given URL it reloads the tab once, which
// re-fetches the current index.html and its up-to-date manifest. A sessionStorage guard keyed by
// the failing URL stops a reload loop when the chunk is genuinely missing (a bad deploy), so that
// case surfaces as an error instead of reloading forever.

const CHUNK_LOAD_ERROR = /Failed to fetch dynamically imported module|error loading dynamically imported module|Importing a module script failed/i
const RELOAD_GUARD_KEY = 'struo.chunkReload'

export function isChunkLoadError(err: unknown): boolean {
  const message = err instanceof Error ? err.message : String(err)
  return CHUNK_LOAD_ERROR.test(message)
}

export interface RecoveryHost {
  location: { assign(url: string): void; reload(): void; href: string }
  addEventListener(type: string, listener: () => void): void
  sessionStorage: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>
}

export function installChunkLoadRecovery(router: Router, host: RecoveryHost = window): void {
  router.onError((err, to) => {
    if (!isChunkLoadError(err)) return
    if (!armReload(host, to.fullPath)) return
    host.location.assign(to.fullPath)
  })
  host.addEventListener('vite:preloadError', () => {
    if (armReload(host, host.location.href)) host.location.reload()
  })
}

// One reload per failing URL per tab session: a chunk that is genuinely gone (bad deploy)
// must surface as an error, not as an infinite reload loop.
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
