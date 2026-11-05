import { apiClient } from './apiClient'

// Wire shapes of the RBAC endpoints (RolesController / UsersController).
export type RolePermissionEntry = {
  collection: string
  canRead: boolean
  canWrite: boolean
  canDelete: boolean
}

// One entry of GET /capabilities: the code plus the locale keys the admin UI translates.
export type CapabilityEntry = { code: string; labelKey: string; descriptionKey: string }

export type EffectivePermissions = {
  isSuperAdmin: boolean
  permissions: Record<string, { read: boolean; write: boolean; delete: boolean }>
}

export const rbacApi = {
  getRolePermissions(roleId: string): Promise<RolePermissionEntry[]> {
    return apiClient.get<RolePermissionEntry[]>(`/roles/${roleId}/permissions`)
  },
  // Full-replace: send every grant the matrix holds; unsent collections lose their grants.
  putRolePermissions(roleId: string, entries: RolePermissionEntry[]): Promise<RolePermissionEntry[]> {
    return apiClient.put<RolePermissionEntry[]>(`/roles/${roleId}/permissions`, entries)
  },
  // GET /capabilities (super-admin only): every system capability a role can hold.
  listCapabilities(): Promise<CapabilityEntry[]> {
    return apiClient.get<CapabilityEntry[]>('/capabilities')
  },
  getRoleCapabilities(roleId: string): Promise<string[]> {
    return apiClient.get<string[]>(`/roles/${roleId}/capabilities`)
  },
  // Full-replace of the role's capability codes; the public role answers 400.
  putRoleCapabilities(roleId: string, codes: string[]): Promise<string[]> {
    return apiClient.put<string[]>(`/roles/${roleId}/capabilities`, codes)
  },
  // roleIds, when provided (including []), asks the backend to compute permissions for that
  // hypothetical role set instead of the user's saved roles — this is what lets the preview
  // follow an unsaved TagSelect edit. Omitted → no query string (unchanged saved-roles behavior).
  getEffectivePermissions(userId: string, roleIds?: string[]): Promise<EffectivePermissions> {
    const query = roleIds !== undefined ? `?roles=${roleIds.join(',')}` : ''
    return apiClient.get<EffectivePermissions>(`/users/${userId}/effective-permissions${query}`)
  },
}
