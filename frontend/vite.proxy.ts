import http from 'node:http'
import type { ProxyOptions } from 'vite'

const API_TARGET = 'http://localhost:5221'

// Same-origin dev/E2E: browser hits /api on the Vite origin, proxied to the API.
// Without an agent the proxy opens a new upstream connection with `Connection: close` per
// request, and on Windows a share of those end in ECONNRESET ("Failed to fetch").
const agent = new http.Agent({ keepAlive: true })

// Every entry keeps the browser's Host (:5173, no `changeOrigin`): the API builds the OIDC
// `redirect_uri` from the request host, and the login starts under /api, so the IdP sends the
// browser back to the dev origin, where the callback paths are proxied too.
export const devProxy: Record<string, ProxyOptions> = {
  '/api': { target: API_TARGET, agent },
  // A key starting with `^` is a regex: the per-provider paths /signin-oidc-{id},
  // /signout-callback-oidc-{id} and /signout-oidc-{id}, plus the bare forms.
  '^/(signin-oidc|signout-callback-oidc|signout-oidc)(-[a-z0-9-]+)?$': { target: API_TARGET, agent },
}
