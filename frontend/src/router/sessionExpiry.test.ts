import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { handleUnauthorized } from './sessionExpiry'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'

vi.mock('../api/apiClient', () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), setUnauthorizedHandler: vi.fn() },
}))

function fakeRouter(name: string, fullPath: string) {
  return {
    currentRoute: { value: { name, fullPath } },
    push: vi.fn().mockResolvedValue(undefined),
  }
}

describe('handleUnauthorized', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('an authenticated 401 expires the session and goes to login with the current path', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u', isSuperAdmin: true, permissions: {} }
    const schema = useSchemaStore()
    schema.collections = [{ name: 'a' } as never]
    schema.loaded = true
    const router = fakeRouter('collection-item', '/collections/article/5?tab=2')

    await handleUnauthorized(auth, router as never)

    expect(auth.sessionExpired).toBe(true)
    expect(auth.user).toBeNull()
    expect(router.push).toHaveBeenCalledWith({
      name: 'login',
      query: { redirect: '/collections/article/5?tab=2', reason: 'expired' },
    })
    expect(schema.loaded).toBe(false)
    expect(schema.collections).toEqual([])
  })

  it('an unauthenticated 401 off the login page keeps going to plain login', async () => {
    const auth = useAuthStore()
    const router = fakeRouter('dashboard', '/')
    await handleUnauthorized(auth, router as never)
    expect(router.push).toHaveBeenCalledWith({ name: 'login' })
    expect(auth.sessionExpired).toBe(false)
  })

  it('an unauthenticated 401 on the login page (wrong password) does nothing', async () => {
    const auth = useAuthStore()
    const router = fakeRouter('login', '/login')
    await handleUnauthorized(auth, router as never)
    expect(router.push).not.toHaveBeenCalled()
    expect(auth.sessionExpired).toBe(false)
  })

  it('later 401s from requests still in flight do not push a second navigation', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u', isSuperAdmin: true, permissions: {} }
    const router = fakeRouter('collection-list', '/collections/article')
    await handleUnauthorized(auth, router as never)
    await handleUnauthorized(auth, router as never)
    expect(router.push).toHaveBeenCalledTimes(1)
  })

  it('a 401 caused by the logout request itself is ignored', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u', isSuperAdmin: true, permissions: {} }
    auth.loggingOut = true
    const router = fakeRouter('dashboard', '/')
    await handleUnauthorized(auth, router as never)
    expect(router.push).not.toHaveBeenCalled()
    expect(auth.sessionExpired).toBe(false)
  })
})
