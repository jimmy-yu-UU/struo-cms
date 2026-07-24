import { apiClient } from './apiClient'

// Wire shapes of the Batch B RBAC endpoints (RolesController / UsersController).
export type RolePermissionEntry = {
  collection: string
  canRead: boolean
  canWrite: boolean
  canDelete: boolean
}

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
  getEffectivePermissions(userId: string): Promise<EffectivePermissions> {
    return apiClient.get<EffectivePermissions>(`/users/${userId}/effective-permissions`)
  },
}
