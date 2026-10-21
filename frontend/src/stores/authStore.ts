import { defineStore } from 'pinia'
import { apiClient, ApiError } from '../api/apiClient'
import type { CollectionPermission } from '../types/schema'

export type CurrentUser = {
  id: string
  email?: string | null
  name?: string | null
  isSuperAdmin: boolean
  permissions: Record<string, CollectionPermission>
}

export const useAuthStore = defineStore('auth', {
  state: () => ({
    user: null as CurrentUser | null,
    // Set when a 401 ended a signed-in session; unsaved-changes guards read it to let the
    // redirect to login through. Cleared by the next successful sign-in.
    sessionExpired: false,
    // True from the start of a logout until the caller's navigation to login settles: the logout
    // request's own 401 is not taken for an expiry, and unsaved-changes guards let the logout
    // through (it drops edits, like an expiry). The caller clears it with endLogout().
    loggingOut: false,
  }),
  getters: {
    skipsLeaveGuards: (state) => state.sessionExpired || state.loggingOut,
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
      if (this.user) this.sessionExpired = false
    },
    // Always ends signed out locally. A 401 means there was no server session left to end, which is
    // the goal already met; any other failure is rethrown after the local sign-out so the caller can
    // say the server side did not complete.
    async logout(): Promise<void> {
      this.loggingOut = true
      try {
        await apiClient.post('/auth/logout')
      } catch (e) {
        if (!(e instanceof ApiError && e.status === 401)) throw e
      } finally {
        this.user = null
      }
    },
    endLogout(): void {
      this.loggingOut = false
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
