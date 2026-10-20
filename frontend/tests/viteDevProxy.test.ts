import { describe, it, expect } from 'vitest'
import { devProxy } from '../vite.proxy'

const proxy = devProxy as Record<string, { target?: string; changeOrigin?: boolean }>

describe('vite dev proxy', () => {
  it.each(['/api', '/signin-oidc', '/signout-callback-oidc'])('forwards %s to the API', (path) => {
    expect(proxy[path]?.target).toBe('http://localhost:5221')
  })

  it.each(['/api', '/signin-oidc', '/signout-callback-oidc'])(
    'keeps the browser Host on %s so the OIDC redirect_uri stays on the dev origin',
    (path) => {
      expect(proxy[path]?.changeOrigin).not.toBe(true)
    },
  )
})
