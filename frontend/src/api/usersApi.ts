import { apiClient } from './apiClient'
import { apiPath } from './apiPath'

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
  // POST /api/users (write on user): the one endpoint that hashes the password and applies the
  // password policy when a user row is created. 409 CONFLICT = email already in use.
  create(body: CreateUserBody): Promise<CreatedUser> {
    return apiClient.post<CreatedUser>('/users', body)
  },

  // GET /api/users/{id}/access-token (read on user): issue and last-use times, never the token. The
  // methods taking an id are async so that a path apiPath refuses is a rejected promise.
  async getAccessTokenInfo(userId: string): Promise<AccessTokenInfo> {
    return apiClient.get<AccessTokenInfo>(apiPath('users', userId, 'access-token'))
  },

  // POST /api/users/{id}/access-token (write on user): issues a token, replacing any previous one.
  // The raw value is in this response only; the server keeps just its hash.
  async generateAccessToken(userId: string): Promise<{ token: string }> {
    return apiClient.post<{ token: string }>(apiPath('users', userId, 'access-token'))
  },

  // DELETE /api/users/{id}/access-token (write on user): returns 204, no body.
  async revokeAccessToken(userId: string): Promise<void> {
    return apiClient.delete<void>(apiPath('users', userId, 'access-token'))
  },

  // Returns 204: no body. The API decides self-service vs admin-reset from whether {userId} is the
  // caller, so this client does not need (or get) a mode flag.
  async changePassword(userId: string, body: ChangePasswordBody): Promise<void> {
    return apiClient.put<void>(apiPath('users', userId, 'password'), body)
  },
}
