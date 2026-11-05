import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from './authStore'

describe('authStore.hasCapability', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('is false when signed out', () => {
    expect(useAuthStore().hasCapability('login.unlock')).toBe(false)
  })

  it('is true for a super-admin whatever the list says', () => {
    const store = useAuthStore()
    store.user = { id: '1', isSuperAdmin: true, permissions: {}, capabilities: [] }
    expect(store.hasCapability('login.unlock')).toBe(true)
  })

  it('is true for a non-admin whose capabilities include the code', () => {
    const store = useAuthStore()
    store.user = { id: '1', isSuperAdmin: false, permissions: {}, capabilities: ['login.unlock'] }
    expect(store.hasCapability('login.unlock')).toBe(true)
  })

  it('is false for a non-admin without the code, or when the list is absent', () => {
    const store = useAuthStore()
    store.user = { id: '1', isSuperAdmin: false, permissions: {}, capabilities: ['other'] }
    expect(store.hasCapability('login.unlock')).toBe(false)
    store.user = { id: '1', isSuperAdmin: false, permissions: {} }
    expect(store.hasCapability('login.unlock')).toBe(false)
  })
})
