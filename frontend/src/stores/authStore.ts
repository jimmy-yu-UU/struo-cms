import { defineStore } from 'pinia'
import { apiClient, ApiError } from '../api/apiClient'
import { i18n } from '../i18n'
import { useToast } from '../composables/useToast'
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
    // True while the last /auth/me failed for a reason other than 401 (server error, network):
    // the session state is unknown, not signed out. Cleared by the next /auth/me that answers.
    connectionError: false,
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
      if (!this.user && this.connectionError) {
        this.connectionError = false
        throw new Error(i18n.global.t('errors.serverUnreachable'))
      }
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
    // A 401 ended a signed-in session: drop the user and let leave guards through.
    markSessionExpired(): void {
      this.sessionExpired = true
      this.user = null
    },
    endLogout(): void {
      this.loggingOut = false
    },
    // Only a 401 means "no session". Any other failure leaves the current user in place and flags
    // connectionError; a signed-in user also gets a notice (boot has no shell to show one in).
    async fetchCurrentUser(): Promise<void> {
      try {
        this.user = await apiClient.get<CurrentUser>('/auth/me')
        this.connectionError = false
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) {
          this.user = null
          this.connectionError = false
          return
        }
        this.connectionError = true
        if (this.user) {
          useToast().add({ severity: 'error', summary: i18n.global.t('errors.serverUnreachable') })
        }
      }
    },
  },
})
