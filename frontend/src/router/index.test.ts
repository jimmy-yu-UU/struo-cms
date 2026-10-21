import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import router from './index'
import { useAuthStore } from '../stores/authStore'

vi.mock('../api/apiClient', () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), setUnauthorizedHandler: vi.fn() },
}))

describe('router guards', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    await router.push('/login')
  })

  it('sends a signed-out visitor to /collections/user/new to login, keeping the target', async () => {
    await router.push('/collections/user/new')
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/collections/user/new')
  })

  it.each(['user', 'file'])('sends a signed-in visitor to /collections/%s/new to that list', async (name) => {
    useAuthStore().user = { id: 'u', isSuperAdmin: true, permissions: {} }
    await router.push(`/collections/${name}/new`)
    expect(router.currentRoute.value.name).toBe('collection-list')
    expect(router.currentRoute.value.params.name).toBe(name)
  })

  it('opens the create form for another collection', async () => {
    useAuthStore().user = { id: 'u', isSuperAdmin: true, permissions: {} }
    await router.push('/collections/article/new')
    expect(router.currentRoute.value.name).toBe('collection-create')
  })
})
