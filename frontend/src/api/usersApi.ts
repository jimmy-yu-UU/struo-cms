import { apiClient } from './apiClient'

export type ChangePasswordBody = {
  newPassword: string
  // Required by the API when the target is the caller themselves, and rejected as meaningless
  // otherwise — so the caller omits it for an admin reset rather than sending an empty string.
  currentPassword?: string
}

export const usersApi = {
  // Returns 204: no body. The API decides self-service vs admin-reset from whether {userId} is the
  // caller, so this client does not need (or get) a mode flag.
  changePassword(userId: string, body: ChangePasswordBody): Promise<void> {
    return apiClient.put<void>(`/users/${userId}/password`, body)
  },
}
