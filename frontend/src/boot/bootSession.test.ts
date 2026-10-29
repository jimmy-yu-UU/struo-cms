import { describe, it, expect, vi } from 'vitest'
import { bootSession } from './bootSession'

type Auth = { user: object | null; connectionError: boolean; fetchCurrentUser: () => Promise<void> }

function makeAuth(outcomes: Array<'ok' | 'down' | 'anon'>): Auth {
  const auth: Auth = {
    user: null,
    connectionError: false,
    fetchCurrentUser: vi.fn(async () => {
      const o = outcomes.shift() ?? 'ok'
      auth.connectionError = o === 'down'
      auth.user = o === 'ok' ? { id: 'u1' } : null
    }),
  }
  return auth
}

describe('bootSession', () => {
  it('proceeds without showing the failure screen when the session check succeeds', async () => {
    const auth = makeAuth(['ok'])
    const waitForRetry = vi.fn(async () => undefined)
    await bootSession(auth, vi.fn(async () => undefined), waitForRetry)
    expect(waitForRetry).not.toHaveBeenCalled()
  })

  it('proceeds on a 401 (signed out) without the failure screen', async () => {
    const auth = makeAuth(['anon'])
    const waitForRetry = vi.fn(async () => undefined)
    await bootSession(auth, vi.fn(async () => undefined), waitForRetry)
    expect(waitForRetry).not.toHaveBeenCalled()
    expect(auth.user).toBeNull()
  })

  it('shows the failure screen on a connection error and re-runs the check on retry', async () => {
    const auth = makeAuth(['down', 'ok'])
    const waitForRetry = vi.fn(async () => undefined)
    await bootSession(auth, vi.fn(async () => undefined), waitForRetry)
    expect(waitForRetry).toHaveBeenCalledTimes(1)
    expect(auth.fetchCurrentUser).toHaveBeenCalledTimes(2)
    expect(auth.user).toEqual({ id: 'u1' })
  })

  it('keeps showing the screen while the check keeps failing, with no automatic retry', async () => {
    const auth = makeAuth(['down', 'down', 'ok'])
    const waitForRetry = vi.fn(async () => undefined)
    await bootSession(auth, vi.fn(async () => undefined), waitForRetry)
    expect(waitForRetry).toHaveBeenCalledTimes(2)
    expect(auth.fetchCurrentUser).toHaveBeenCalledTimes(3)
  })

  it('does not call the check again until the user retries', async () => {
    const auth = makeAuth(['down', 'ok'])
    let release: () => void = () => undefined
    const waitForRetry = vi.fn(() => new Promise<void>((r) => { release = r }))
    const done = bootSession(auth, vi.fn(async () => undefined), waitForRetry)
    await Promise.resolve()
    await Promise.resolve()
    expect(auth.fetchCurrentUser).toHaveBeenCalledTimes(1)
    release()
    await done
    expect(auth.fetchCurrentUser).toHaveBeenCalledTimes(2)
  })
})
