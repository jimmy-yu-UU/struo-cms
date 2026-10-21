import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from './authStore'
import { apiClient, ApiError } from '../api/apiClient'

vi.mock('../api/apiClient', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/apiClient')>()),
  apiClient: { get: vi.fn(), post: vi.fn(), setUnauthorizedHandler: vi.fn() },
}))

describe('authStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('login success sets the user and isAuthenticated', async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ id: 'u1', isSuperAdmin: false, permissions: {} })
    vi.mocked(apiClient.get).mockResolvedValue({ id: 'u1', isSuperAdmin: false, permissions: {} }) // follow-up fetchCurrentUser
    const store = useAuthStore()
    await store.login('a@b.com', 'pw')
    expect(store.user).toEqual({ id: 'u1', isSuperAdmin: false, permissions: {} })
    expect(store.isAuthenticated).toBe(true)
    expect(apiClient.post).toHaveBeenCalledWith('/auth/login', { email: 'a@b.com', password: 'pw' })
  })

  it('login failure leaves user null and rethrows', async () => {
    vi.mocked(apiClient.post).mockRejectedValue(new Error('Invalid credentials.'))
    const store = useAuthStore()
    await expect(store.login('a@b.com', 'bad')).rejects.toThrow('Invalid credentials.')
    expect(store.isAuthenticated).toBe(false)
  })

  it('fetchCurrentUser sets user on 200', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ id: 'u9', isSuperAdmin: false, permissions: {} })
    const store = useAuthStore()
    await store.fetchCurrentUser()
    expect(store.user).toEqual({ id: 'u9', isSuperAdmin: false, permissions: {} })
  })

  it('fetchCurrentUser clears user on error (401)', async () => {
    vi.mocked(apiClient.get).mockRejectedValue(new Error('unauth'))
    const store = useAuthStore()
    await store.fetchCurrentUser()
    expect(store.user).toBeNull()
    expect(store.isAuthenticated).toBe(false)
  })

  it('logout clears the user', async () => {
    vi.mocked(apiClient.post).mockResolvedValue(undefined)
    const store = useAuthStore()
    store.user = { id: 'u1', isSuperAdmin: false, permissions: {} }
    await store.logout()
    expect(store.user).toBeNull()
  })

  it('canRead is true for super-admin on any collection', () => {
    const store = useAuthStore()
    store.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    expect(store.canRead('anything')).toBe(true)
  })

  it('canRead reflects per-collection read grant for non-super users', () => {
    const store = useAuthStore()
    store.user = { id: 'u1', isSuperAdmin: false, permissions: { article: { read: true, write: false, delete: false } } }
    expect(store.canRead('article')).toBe(true)
    expect(store.canRead('category')).toBe(false)
  })

  it('canRead is false when unauthenticated', () => {
    const store = useAuthStore()
    expect(store.canRead('article')).toBe(false)
  })

  it('canWrite: super-admin true; limited user by grant', () => {
    const store = useAuthStore()
    store.user = { id: '1', isSuperAdmin: true, permissions: {} }
    expect(store.canWrite('article')).toBe(true)
    store.user = { id: '2', isSuperAdmin: false, permissions: { article: { read: true, write: true, delete: false } } }
    expect(store.canWrite('article')).toBe(true)
    expect(store.canDelete('article')).toBe(false)
    expect(store.canWrite('category')).toBe(false)
  })

  it('canWrite false when unauthenticated', () => {
    const store = useAuthStore()
    store.user = null
    expect(store.canWrite('article')).toBe(false)
  })

  it('logout clears the user; a 401 from the server counts as success', async () => {
    vi.mocked(apiClient.post).mockRejectedValue(new ApiError(401, 'no session'))
    const store = useAuthStore()
    store.user = { id: 'u1', isSuperAdmin: false, permissions: {} }
    await expect(store.logout()).resolves.toBeUndefined()
    expect(store.user).toBeNull()
    expect(store.loggingOut).toBe(true) // stays up until the caller's navigation settles
  })

  it('logout still clears the user and rethrows a server failure', async () => {
    vi.mocked(apiClient.post).mockRejectedValue(new ApiError(500, 'boom'))
    const store = useAuthStore()
    store.user = { id: 'u1', isSuperAdmin: false, permissions: {} }
    await expect(store.logout()).rejects.toMatchObject({ status: 500 })
    expect(store.user).toBeNull()
    expect(store.loggingOut).toBe(true)
    store.endLogout()
    expect(store.loggingOut).toBe(false)
  })

  it('leave guards are skipped while the session is expired or a logout is in progress', () => {
    const store = useAuthStore()
    expect(store.skipsLeaveGuards).toBe(false)
    store.sessionExpired = true
    expect(store.skipsLeaveGuards).toBe(true)
    store.sessionExpired = false
    store.loggingOut = true
    expect(store.skipsLeaveGuards).toBe(true)
  })

  it('a successful sign-in clears the session-expired flag', async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ id: 'u1' })
    vi.mocked(apiClient.get).mockResolvedValue({ id: 'u1', isSuperAdmin: false, permissions: {} })
    const store = useAuthStore()
    store.sessionExpired = true
    await store.login('a@b.com', 'pw')
    expect(store.sessionExpired).toBe(false)
  })
})
