import { apiClient } from './apiClient'

export type ChangePasswordBody = {
  newPassword: string
  // Required by the API when the target is the caller themselves — a missing or empty value
  // there produces 400 INVALID_CURRENT_PASSWORD — but silently ignored on an admin reset, so the
  // caller omits it for an admin reset rather than sending an empty string.
  currentPassword?: string
}

export type CreateUserBody = { email: string; password: string; name?: string }
export type AccessTokenInfo = {
  hasToken: boolean
  // UTC ISO strings; both null when the user has no token, lastUsedAt also null until first use.
  createdAt: string | null
  lastUsedAt: string | null
}
export type CreatedUser = { id: string; email: string; name?: string | null }

export const usersApi = {
  // POST /api/users (super-admin only): the one endpoint that hashes the password and applies the
  // password policy when a user row is created. 409 CONFLICT = email already in use.
  create(body: CreateUserBody): Promise<CreatedUser> {
    return apiClient.post<CreatedUser>('/users', body)
  },

  // GET /api/users/{id}/access-token (super-admin only): issue and last-use times, never the token.
  getAccessTokenInfo(userId: string): Promise<AccessTokenInfo> {
    return apiClient.get<AccessTokenInfo>(`/users/${userId}/access-token`)
  },

  // Returns 204: no body. The API decides self-service vs admin-reset from whether {userId} is the
  // caller, so this client does not need (or get) a mode flag.
  changePassword(userId: string, body: ChangePasswordBody): Promise<void> {
    return apiClient.put<void>(`/users/${userId}/password`, body)
  },
}
