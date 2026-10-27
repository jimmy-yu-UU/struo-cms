import { loginRedirectQuery } from '../lib/safeRedirect'

export type GuardTarget = { name: string | null | undefined; meta: { public?: boolean }; fullPath?: string }
export type GuardResult = true | { name: string; query?: { redirect: string } }

export function authGuard(to: GuardTarget, isAuthenticated: boolean): GuardResult {
  const isPublic = to.meta.public === true
  if (!isAuthenticated && !isPublic) {
    const query = loginRedirectQuery(to.fullPath)
    return query ? { name: 'login', query } : { name: 'login' }
  }
  if (isAuthenticated && to.name === 'login') return { name: 'dashboard' }
  return true
}

export type CreateRouteTarget = { name: string | null | undefined; params: { name?: unknown } }
export type CreateRouteResult = true | { name: 'collection-list'; params: { name: string } }

// `creatable` is the schema's flag for the target collection. Rows of a collection whose flag is
// false are not created through the generic form, so a direct visit to the create URL goes back to
// the list. An unknown flag (unknown collection, schema not loaded) lets the form open.
export function createRouteGuard(to: CreateRouteTarget, creatable: boolean | undefined): CreateRouteResult {
  const collection = to.params.name
  if (to.name === 'collection-create' && typeof collection === 'string' && creatable === false) {
    return { name: 'collection-list', params: { name: collection } }
  }
  return true
}
