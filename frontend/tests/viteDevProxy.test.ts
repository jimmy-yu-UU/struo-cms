import { describe, it, expect } from 'vitest'
import { devProxy } from '../vite.proxy'

type Entry = { target?: string; changeOrigin?: boolean; agent?: { keepAlive?: boolean } }
const proxy = devProxy as Record<string, Entry>
const OIDC_KEY = '^/(signin-oidc|signout-callback-oidc|signout-oidc)(-[a-z0-9-]+)?$'
const PATHS = ['/api', OIDC_KEY]

describe('vite dev proxy', () => {
  it.each(PATHS)('forwards %s to the API', (path) => {
    expect(proxy[path]?.target).toBe('http://localhost:5221')
  })

  it.each(PATHS)(
    'keeps the browser Host on %s so the OIDC redirect_uri stays on the dev origin',
    (path) => {
      expect(proxy[path]?.changeOrigin).not.toBe(true)
    },
  )

  it.each(PATHS)('reuses upstream connections on %s (keep-alive agent)', (path) => {
    expect(proxy[path]?.agent?.keepAlive).toBe(true)
  })

  it.each([
    '/signin-oidc', '/signin-oidc-google', '/signout-callback-oidc', '/signout-callback-oidc-google',
    '/signout-oidc', '/signout-oidc-google',
  ])('the OIDC key matches %s', (path) => {
    expect(new RegExp(OIDC_KEY).test(path)).toBe(true)
  })

  it.each(['/signin', '/signin-oidcx', '/signin-oidc/', '/signin-oidc-Google', '/login', '/api/x'])(
    'the OIDC key leaves %s alone',
    (path) => {
      expect(new RegExp(OIDC_KEY).test(path)).toBe(false)
    },
  )
})
