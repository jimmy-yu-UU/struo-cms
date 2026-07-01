import { describe, it, expect } from 'vitest'
import { authGuard } from './guard'

describe('authGuard', () => {
  it('redirects unauthenticated users on protected routes to login', () => {
    expect(authGuard({ name: 'dashboard', meta: {} }, false)).toEqual({ name: 'login' })
  })

  it('allows unauthenticated users on public routes', () => {
    expect(authGuard({ name: 'login', meta: { public: true } }, false)).toBe(true)
  })

  it('redirects authenticated users away from login to dashboard', () => {
    expect(authGuard({ name: 'login', meta: { public: true } }, true)).toEqual({ name: 'dashboard' })
  })

  it('allows authenticated users on protected routes', () => {
    expect(authGuard({ name: 'dashboard', meta: {} }, true)).toBe(true)
  })
})
