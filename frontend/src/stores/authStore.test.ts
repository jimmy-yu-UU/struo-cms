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
    vi.mocked(apiClient.post).mockResolvedValue({ id: 'u1' })
    vi.mocked(apiClient.get).mockResolvedValue({ id: 'u1' }) // follow-up fetchCurrentUser
    const store = useAuthStore()
    await store.login('a@b.com', 'pw')
    expect(store.user).toEqual({ id: 'u1' })
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
    vi.mocked(apiClient.get).mockResolvedValue({ id: 'u9' })
    const store = useAuthStore()
    await store.fetchCurrentUser()
    expect(store.user).toEqual({ id: 'u9' })
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
    store.user = { id: 'u1' }
    await store.logout()
    expect(store.user).toBeNull()
  })
})
