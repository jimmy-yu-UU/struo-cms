import { loginRedirectQuery } from '../lib/safeRedirect'
import { NON_CREATABLE_COLLECTIONS } from '../lib/frameworkCollections'

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

// Rows of these collections are not created through the generic form, so a direct visit to the
// create URL goes back to the list.
export function createRouteGuard(to: CreateRouteTarget): CreateRouteResult {
  const collection = to.params.name
  if (to.name === 'collection-create' && typeof collection === 'string' && NON_CREATABLE_COLLECTIONS.has(collection)) {
    return { name: 'collection-list', params: { name: collection } }
  }
  return true
}
