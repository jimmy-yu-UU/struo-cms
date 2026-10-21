// camelCase route/collection name for the framework's `[CmsCollection("Language")]` entity.
export const LANGUAGE_COLLECTION = 'language'

// Framework collections with dedicated RBAC editors on the generic item form.
export const ROLE_COLLECTION = 'role'
export const USER_COLLECTION = 'user'

// Collections the generic items API refuses to create rows in: files come from the upload endpoint
// (POST /api/files), users from POST /api/users. The list view offers no generic "New" form for
// them; for `user` it opens a create-user dialog instead, for super-admins only.
export const NON_CREATABLE_COLLECTIONS: ReadonlySet<string> = new Set([USER_COLLECTION, 'file'])
