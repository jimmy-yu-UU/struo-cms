import { describe, it, expect } from 'vitest'
import { authGuard, createRouteGuard } from './guard'

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

describe('authGuard redirect', () => {
  it('carries the target full path to login for an unauthenticated user', () => {
    expect(authGuard({ name: 'collection-item', meta: {}, fullPath: '/collections/article/5?tab=2' }, false))
      .toEqual({ name: 'login', query: { redirect: '/collections/article/5?tab=2' } })
  })

  it('omits redirect when the target is the dashboard root', () => {
    expect(authGuard({ name: 'dashboard', meta: {}, fullPath: '/' }, false)).toEqual({ name: 'login' })
  })

  it('omits redirect when the target path is not a same-origin path', () => {
    expect(authGuard({ name: 'x', meta: {}, fullPath: '//evil' }, false)).toEqual({ name: 'login' })
  })
})

describe('createRouteGuard', () => {
  it.each(['user', 'file'])('sends a new %s back to its list', (name) => {
    expect(createRouteGuard({ name: 'collection-create', params: { name } }))
      .toEqual({ name: 'collection-list', params: { name } })
  })

  it('lets other collections open the create form', () => {
    expect(createRouteGuard({ name: 'collection-create', params: { name: 'article' } })).toBe(true)
  })

  it('ignores routes other than the create form', () => {
    expect(createRouteGuard({ name: 'collection-item', params: { name: 'user' } })).toBe(true)
  })
})
