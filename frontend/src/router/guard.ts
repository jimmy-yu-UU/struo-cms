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
