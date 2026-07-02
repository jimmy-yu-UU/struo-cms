import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from './authStore'
import { apiClient } from '../api/apiClient'

vi.mock('../api/apiClient', () => ({
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
})
