export type GuardTarget = { name: string | null | undefined; meta: { public?: boolean } }
export type GuardResult = true | { name: string }

export function authGuard(to: GuardTarget, isAuthenticated: boolean): GuardResult {
  const isPublic = to.meta.public === true
  if (!isAuthenticated && !isPublic) return { name: 'login' }
  if (isAuthenticated && to.name === 'login') return { name: 'dashboard' }
  return true
}
