import { safeRedirect } from '../lib/safeRedirect'

export type GuardTarget = { name: string | null | undefined; meta: { public?: boolean }; fullPath?: string }
export type GuardResult = true | { name: string; query?: { redirect: string } }

export function authGuard(to: GuardTarget, isAuthenticated: boolean): GuardResult {
  const isPublic = to.meta.public === true
  if (!isAuthenticated && !isPublic) return toLogin(to.fullPath)
  if (isAuthenticated && to.name === 'login') return { name: 'dashboard' }
  return true
}

// The requested page rides along so sign-in can return to it; the dashboard root is the default
// landing anyway, and an unsafe path is dropped rather than carried.
function toLogin(fullPath: string | undefined): GuardResult {
  const redirect = safeRedirect(fullPath)
  return redirect && redirect !== '/' ? { name: 'login', query: { redirect } } : { name: 'login' }
}
