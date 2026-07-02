import { defineStore } from 'pinia'
import { apiClient } from '../api/apiClient'

export type CurrentUser = { id: string }

export const useAuthStore = defineStore('auth', {
  state: () => ({ user: null as CurrentUser | null }),
  getters: {
    isAuthenticated: (state) => state.user !== null,
  },
  actions: {
    async login(email: string, password: string): Promise<void> {
      // Login returns { id }; then confirm via /me for a canonical session.
      await apiClient.post<CurrentUser>('/auth/login', { email, password })
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
