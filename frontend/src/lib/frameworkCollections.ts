// camelCase route/collection name for the framework's `[CmsCollection("Language")]` entity.
export const LANGUAGE_COLLECTION = 'language'

// Framework collections with dedicated RBAC editors on the generic item form.
export const ROLE_COLLECTION = 'role'
export const USER_COLLECTION = 'user'

// Hidden junction collections that follow the grant of the collection that owns them (a role
// assignment is a write on `user`, a role's grants a write on `role`), so they get no matrix row.
export const GOVERNED_JUNCTIONS: readonly string[] = ['userRole', 'permission']
