import { describe, it, expect } from 'vitest'
import { devProxy } from '../vite.proxy'

type Entry = { target?: string; changeOrigin?: boolean; agent?: { keepAlive?: boolean } }
const proxy = devProxy as Record<string, Entry>
const PATHS = ['/api', '/signin-oidc', '/signout-callback-oidc']

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
})
