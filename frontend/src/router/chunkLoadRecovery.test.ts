import { describe, it, expect, vi } from 'vitest'
import type { Router } from 'vue-router'
import { isChunkLoadError, installChunkLoadRecovery, type RecoveryHost } from './chunkLoadRecovery'

describe('isChunkLoadError', () => {
  it('is true for a "Failed to fetch dynamically imported module" Error', () => {
    expect(isChunkLoadError(new Error('Failed to fetch dynamically imported module: http://x/assets/a.js'))).toBe(true)
  })

  it('is true for a plain string matching the pattern', () => {
    expect(isChunkLoadError('error loading dynamically imported module')).toBe(true)
  })

  it('is false for an unrelated error', () => {
    expect(isChunkLoadError(new Error('Network Error'))).toBe(false)
  })
})

// A minimal fake host: spies for assign/reload/addEventListener, and an in-memory
// sessionStorage backed by a Map so the reload guard can be observed without jsdom.
function fakeHost() {
  const store = new Map<string, string>()
  const listeners = new Map<string, () => void>()
  const host: RecoveryHost = {
    location: {
      assign: vi.fn(),
      reload: vi.fn(),
      href: 'http://x/',
    },
    addEventListener: vi.fn((type: string, listener: () => void) => {
      listeners.set(type, listener)
    }),
    sessionStorage: {
      getItem: (key: string) => (store.has(key) ? store.get(key)! : null),
      setItem: (key: string, value: string) => {
        store.set(key, value)
      },
      removeItem: (key: string) => {
        store.delete(key)
      },
    },
  }
  return { host, listeners }
}

describe('installChunkLoadRecovery', () => {
  it('reloads to the failing route once when router.onError sees a chunk-load error', () => {
    const onError = vi.fn()
    const router = { onError } as unknown as Router
    const { host } = fakeHost()

    installChunkLoadRecovery(router, host)

    const handler = onError.mock.calls[0][0] as (err: unknown, to: { fullPath: string }) => void
    handler(new Error('Failed to fetch dynamically imported module'), { fullPath: '/media' })

    expect(host.location.assign).toHaveBeenCalledTimes(1)
    expect(host.location.assign).toHaveBeenCalledWith('/media')
  })

  it('does not reload for a non-chunk error', () => {
    const onError = vi.fn()
    const router = { onError } as unknown as Router
    const { host } = fakeHost()

    installChunkLoadRecovery(router, host)

    const handler = onError.mock.calls[0][0] as (err: unknown, to: { fullPath: string }) => void
    handler(new Error('Network Error'), { fullPath: '/media' })

    expect(host.location.assign).not.toHaveBeenCalled()
  })

  it('only reloads once for the same path (reload guard)', () => {
    const onError = vi.fn()
    const router = { onError } as unknown as Router
    const { host } = fakeHost()

    installChunkLoadRecovery(router, host)

    const handler = onError.mock.calls[0][0] as (err: unknown, to: { fullPath: string }) => void
    handler(new Error('Failed to fetch dynamically imported module'), { fullPath: '/media' })
    handler(new Error('Failed to fetch dynamically imported module'), { fullPath: '/media' })

    expect(host.location.assign).toHaveBeenCalledTimes(1)
  })

  it('reloads once on a vite:preloadError event', () => {
    const onError = vi.fn()
    const router = { onError } as unknown as Router
    const { host, listeners } = fakeHost()

    installChunkLoadRecovery(router, host)

    const preloadHandler = listeners.get('vite:preloadError')!
    preloadHandler()

    expect(host.location.reload).toHaveBeenCalledTimes(1)
  })

  it('does not reload twice for a second preloadError event on the same href', () => {
    const onError = vi.fn()
    const router = { onError } as unknown as Router
    const { host, listeners } = fakeHost()

    installChunkLoadRecovery(router, host)

    const preloadHandler = listeners.get('vite:preloadError')!
    preloadHandler()
    preloadHandler()

    expect(host.location.reload).toHaveBeenCalledTimes(1)
  })
})
