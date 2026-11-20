import { apiClient } from './apiClient'
import { apiPath, withQuery } from './apiPath'

// Wire shapes of the RBAC endpoints (RolesController / UsersController).
export type RolePermissionEntry = {
  collection: string
  canRead: boolean
  canWrite: boolean
  canDelete: boolean
}

// What a grants save reports: the grants as stored and the role's new version (absent when the response has none).
export type RoleGrantsSaved = { entries: RolePermissionEntry[]; version: number | undefined }

export type EffectivePermissions = {
  isSuperAdmin: boolean
  permissions: Record<string, { read: boolean; write: boolean; delete: boolean }>
}

export const rbacApi = {
  // async so that a path apiPath refuses is a rejected promise, as for any other failed request.
  async getRolePermissions(roleId: string): Promise<RolePermissionEntry[]> {
    return apiClient.get<RolePermissionEntry[]>(apiPath('roles', roleId, 'permissions'))
  },
  // Full-replace: send every grant the matrix holds; unsent collections lose their grants. `version` is the
  // role version the caller last saw; a role that has moved on answers 409 VERSION_CONFLICT.
  async putRolePermissions(roleId: string, entries: RolePermissionEntry[], version?: number): Promise<RoleGrantsSaved> {
    const params = new URLSearchParams()
    if (version !== undefined) params.set('version', String(version))
    const { data, meta } = await apiClient.putEnveloped<RolePermissionEntry[], { version?: unknown }>(
      withQuery(apiPath('roles', roleId, 'permissions'), params), entries)
    return { entries: data, version: typeof meta?.version === 'number' ? meta.version : undefined }
  },
  // roleIds, when provided (including []), asks the backend to compute permissions for that
  // hypothetical role set instead of the user's saved roles — this is what lets the preview
  // follow an unsaved TagSelect edit. Omitted → no query string (unchanged saved-roles behavior).
  async getEffectivePermissions(userId: string, roleIds?: string[]): Promise<EffectivePermissions> {
    const params = new URLSearchParams()
    if (roleIds !== undefined) params.set('roles', roleIds.join(','))
    return apiClient.get<EffectivePermissions>(withQuery(apiPath('users', userId, 'effective-permissions'), params))
  },
}
