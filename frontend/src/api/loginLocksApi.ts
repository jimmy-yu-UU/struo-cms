import { apiClient } from './apiClient'

// One entry per queried address, in request order. `clearable` is false for a super-admin account when
// the caller is not a super-admin. An address with no account answers in the same shape.
export type LoginLockState = { email: string; locked: boolean; retryAfterSeconds: number; clearable: boolean }

// Read on the user collection shows lock state; write on it clears a lock.
export const loginLocksApi = {
  // At most 100 addresses per call.
  query(emails: string[]): Promise<LoginLockState[]> {
    return apiClient.post<LoginLockState[]>('/login-locks/query', { emails })
  },
  // Returns 204: no body.
  clear(email: string): Promise<void> {
    return apiClient.delete<void>(`/login-locks?email=${encodeURIComponent(email)}`)
  },
}
