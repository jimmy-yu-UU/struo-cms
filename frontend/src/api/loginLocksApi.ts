import { apiClient } from './apiClient'

export type LoginLockState = { locked: boolean; retryAfterSeconds: number }

// Both endpoints need a super-admin or the login.unlock capability. The answer for an email with no
// account has the same shape as for one with an account.
export const loginLocksApi = {
  get(email: string): Promise<LoginLockState> {
    return apiClient.get<LoginLockState>(`/login-locks?email=${encodeURIComponent(email)}`)
  },
  // Returns 204: no body.
  clear(email: string): Promise<void> {
    return apiClient.delete<void>(`/login-locks?email=${encodeURIComponent(email)}`)
  },
}
