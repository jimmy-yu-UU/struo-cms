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

// A minimal fake router: captures the beforeEach/afterEach/onError callbacks vue-router would
// normally own, so tests can drive them directly in whatever order a real navigation would.
function fakeRouter() {
  const callbacks: {
    beforeEach?: () => void
    afterEach?: () => void
    onError?: (err: unknown, to: { fullPath: string }) => void
  } = {}
  const router = {
    beforeEach: vi.fn((cb: () => void) => { callbacks.beforeEach = cb }),
    afterEach: vi.fn((cb: () => void) => { callbacks.afterEach = cb }),
    onError: vi.fn((cb: (err: unknown, to: { fullPath: string }) => void) => { callbacks.onError = cb }),
  } as unknown as Router
  return { router, callbacks }
}

// A minimal fake host: spies for assign/reload/addEventListener, and an in-memory
// sessionStorage backed by a Map so the reload guard can be observed without jsdom.
function fakeHost(initialPath = '/', initialSearch = '') {
  const store = new Map<string, string>()
  const listeners = new Map<string, () => void>()
  const host: RecoveryHost = {
    location: {
      assign: vi.fn(),
      reload: vi.fn(),
      pathname: initialPath,
      search: initialSearch,
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
  it('route failure: onError owns it, assign once, reload not called', () => {
    const { router, callbacks } = fakeRouter()
    const { host, listeners } = fakeHost()

    installChunkLoadRecovery(router, host)

    callbacks.beforeEach!()
    listeners.get('vite:preloadError')!()
    callbacks.onError!(new Error('Failed to fetch dynamically imported module'), { fullPath: '/media' })

    expect(host.location.assign).toHaveBeenCalledTimes(1)
    expect(host.location.assign).toHaveBeenCalledWith('/media')
    expect(host.location.reload).not.toHaveBeenCalled()
  })

  it('repeating the same failing sequence with no afterEach in between: still only one assign total', () => {
    const { router, callbacks } = fakeRouter()
    const { host, listeners } = fakeHost()

    installChunkLoadRecovery(router, host)

    callbacks.beforeEach!()
    listeners.get('vite:preloadError')!()
    callbacks.onError!(new Error('Failed to fetch dynamically imported module'), { fullPath: '/media' })

    callbacks.beforeEach!()
    listeners.get('vite:preloadError')!()
    callbacks.onError!(new Error('Failed to fetch dynamically imported module'), { fullPath: '/media' })

    expect(host.location.assign).toHaveBeenCalledTimes(1)
    expect(host.location.reload).not.toHaveBeenCalled()
  })

  it('after a successful navigation (afterEach) clears the guard, the next failure reloads again', () => {
    const { router, callbacks } = fakeRouter()
    const { host, listeners } = fakeHost()

    installChunkLoadRecovery(router, host)

    callbacks.beforeEach!()
    listeners.get('vite:preloadError')!()
    callbacks.onError!(new Error('Failed to fetch dynamically imported module'), { fullPath: '/media' })

    callbacks.afterEach!()

    callbacks.beforeEach!()
    listeners.get('vite:preloadError')!()
    callbacks.onError!(new Error('Failed to fetch dynamically imported module'), { fullPath: '/media' })

    expect(host.location.assign).toHaveBeenCalledTimes(2)
  })

  it('async-component failure outside navigation: preloadError reloads once', () => {
    const { router } = fakeRouter()
    const { host, listeners } = fakeHost('/collections/article/x', '')

    installChunkLoadRecovery(router, host)

    listeners.get('vite:preloadError')!()

    expect(host.location.reload).toHaveBeenCalledTimes(1)
    expect(host.location.assign).not.toHaveBeenCalled()
  })

  it('a second preloadError with no afterEach in between still reloads only once', () => {
    const { router } = fakeRouter()
    const { host, listeners } = fakeHost('/collections/article/x', '')

    installChunkLoadRecovery(router, host)

    listeners.get('vite:preloadError')!()
    listeners.get('vite:preloadError')!()

    expect(host.location.reload).toHaveBeenCalledTimes(1)
  })

  it('a non-chunk error in onError does not assign and does not touch the guard', () => {
    const { router, callbacks } = fakeRouter()
    const { host, listeners } = fakeHost()

    installChunkLoadRecovery(router, host)

    callbacks.beforeEach!()
    callbacks.onError!(new Error('Network Error'), { fullPath: '/media' })
    expect(host.location.assign).not.toHaveBeenCalled()

    // A following chunk error still gets its one reload -- the guard was untouched.
    callbacks.beforeEach!()
    listeners.get('vite:preloadError')!()
    callbacks.onError!(new Error('Failed to fetch dynamically imported module'), { fullPath: '/media' })

    expect(host.location.assign).toHaveBeenCalledTimes(1)
    expect(host.location.assign).toHaveBeenCalledWith('/media')
  })
})
