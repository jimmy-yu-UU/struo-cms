const BACKSLASH = '\\'

// Browsers strip tab, CR and LF from a URL, so "/<tab>/host" would turn into "//host"; any control
// character is refused.
function hasControlChar(value: string): boolean {
  for (const ch of value) {
    const code = ch.codePointAt(0) ?? 0
    if (code < 0x20 || code === 0x7f) return true
  }
  return false
}

// Accepts only a same-origin path: it starts with "/" but not "//" (protocol-relative) or "/\"
// (which browsers read as "//"), and holds no control characters. Mirrors the site-relative rule
// of the server's LocalRedirect, which re-checks the OIDC returnUrl.
export function safeRedirect(value: unknown): string | null {
  if (typeof value !== 'string' || value === '') return null
  if (!value.startsWith('/') || value.startsWith('//') || value.startsWith('/' + BACKSLASH)) return null
  if (hasControlChar(value)) return null
  return value
}

// The query a login redirect carries: the requested page, unless it is the default landing (the
// dashboard root), the login page itself, or not a safe path. Shared by the router guard and the
// session-expiry handler so both build the same location.
export function loginRedirectQuery(fullPath: string | undefined): { redirect: string } | undefined {
  const redirect = safeRedirect(fullPath)
  if (redirect === null || redirect === '/' || redirect === '/login' || redirect.startsWith('/login?')) return undefined
  return { redirect }
}

// Query-string encoding for the SSO returnUrl. Slashes stay readable, as they are legal in a query.
export function encodeRedirectParam(path: string): string {
  return encodeURIComponent(path).replaceAll('%2F', '/')
}
