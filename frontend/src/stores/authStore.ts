import { defineStore } from 'pinia'
import { apiClient } from '../api/apiClient'
import type { CollectionPermission } from '../types/schema'

export type CurrentUser = {
  id: string
  email?: string | null
  name?: string | null
  isSuperAdmin: boolean
  permissions: Record<string, CollectionPermission>
}

export const useAuthStore = defineStore('auth', {
  state: () => ({ user: null as CurrentUser | null }),
  getters: {
    isAuthenticated: (state) => state.user !== null,
    canRead: (state) => (collection: string): boolean =>
      !!state.user && (state.user.isSuperAdmin || state.user.permissions?.[collection]?.read === true),
    canWrite: (state) => (collection: string): boolean =>
      !!state.user && (state.user.isSuperAdmin || state.user.permissions?.[collection]?.write === true),
    canDelete: (state) => (collection: string): boolean =>
      !!state.user && (state.user.isSuperAdmin || state.user.permissions?.[collection]?.delete === true),
  },
  actions: {
    async login(email: string, password: string): Promise<void> {
      // Login returns { id }; then confirm via /me for a canonical session (id + perms).
      await apiClient.post('/auth/login', { email, password })
      await this.fetchCurrentUser()
    },
    async logout(): Promise<void> {
      try {
        await apiClient.post('/auth/logout')
      } finally {
        this.user = null
      }
    },
    async fetchCurrentUser(): Promise<void> {
      try {
        this.user = await apiClient.get<CurrentUser>('/auth/me')
      } catch {
        this.user = null // unauthenticated / no session
      }
    },
  },
})
